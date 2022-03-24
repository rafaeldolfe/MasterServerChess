/*
MIT License

Copyright (c) 2019 Radek Lžičař

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MasterServerToolkit.Bridges.MirrorNetworking;
using MasterServerToolkit.MasterServer;
using Mirror;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace Chessticle
{
    public class GameManager : NetworkBehaviour
    {
        [SerializeField] private RoomNetworkManager roomNetworkManager;
        [SerializeField] private float abortTime = 30f;
        [SerializeField] private float informationDumpsPerSecond = 3;
        [SerializeField] private float afterGameLobbyCloseTime = 60f;

        private MoveResult lastConfirmedMoveResult;

        private const int START_CLOCK_TIME = 10 * 60;
        private Clock m_Clock;
        private bool hasGameStarted;
        private ServerState? m_PreviousState;
        private bool m_PendingDrawOfferByOpponent;

        [SerializeField] private ChessboardUI chessboardUI;

        // SERVER VARIABLES
        private ServerState gameServerState;

        private readonly Dictionary<uint, PlayerData> netIdToPlayer = new Dictionary<uint, PlayerData>();
        private PlayerData player1;
        private PlayerData player2;
        private PlayerData playerWhite;
        private PlayerData playerBlack;

        private PlayerData serverCurrentPlayer;
        private PlayerData serverPreviousPlayer;

        private double gameStartTime;
        private double lastMoveTime;

        private Queue<ClientInput> receivedInputs = new Queue<ClientInput>();

        private bool clientMoved;
        private bool gameIsStopped;


        // CLIENT VARIABLES
        private ClientState gameClientState;

        private double timeLeft;
        private ChessColor localPlayerChessColor;
        private ChessColor opponentChessColor;
        private bool isMyTurn;
        private MoveResult clientConfirmedMoveResult;
        private ChessColor clientOutOfTimePlayerColor;
        private ChessColor clientResigningPlayerColor;
        private bool wonByWalkover;
        private bool drawAgreed;

        private void Awake()
        {
            if (!isServerOnly)
            {
                chessboardUI.CmdPlayerMoved += CmdPlayerMoved;
                chessboardUI.CmdOpponentMoveFinished += OnMoveFinished;
                chessboardUI.CmdResignationRequested += CmdResign;
                chessboardUI.CmdClaimDrawRequested += CmdOfferDraw;
                chessboardUI.CmdOfferDrawRequested += CmdOfferDraw;
                chessboardUI.RefreshClaimDrawButton();
            }
        }

        private void OnDestroy()
        {
            if (!isServerOnly)
            {
                chessboardUI.CmdPlayerMoved -= CmdPlayerMoved;
                chessboardUI.CmdOpponentMoveFinished -= OnMoveFinished;
                chessboardUI.CmdResignationRequested -= CmdResign;
                chessboardUI.CmdClaimDrawRequested -= CmdOfferDraw;
                chessboardUI.CmdOfferDrawRequested -= CmdOfferDraw;
            }
        }

        private void Start()
        {
            m_Clock = new Clock(START_CLOCK_TIME);
            StartCoroutine(StartGameLoop());
        }

        private IEnumerator StartGameLoop()
        {
            if (isServerOnly)
            {
                yield return ServerGameLoop();
            }
            else
            {
                yield return ClientGameLoop();
            }
        }

        private PlayerData NextPlayer
        {
            get
            {
                if (serverCurrentPlayer == null)
                    throw new ArgumentNullException("Cannot get next player when current player is null");
                return serverCurrentPlayer == player1 ? player2 : player1;
            }
        }

        private static ChessColor OpponentColorOf(ChessColor chessColor)
        {
            return chessColor == ChessColor.White ? ChessColor.Black : ChessColor.White;
        }

        private PlayerData OpponentPlayer(PlayerData player)
        {
            return player.chessColor == ChessColor.White ? playerBlack : playerWhite;
        }

        private ChessColor NetIdToColor(uint existingPlayerNetId)
        {
            return netIdToPlayer[existingPlayerNetId].chessColor;
        }

        #region Commands

        [Client]
        private void CmdPlayerMoved(uint playerNetId, int startIdx, int targetIdx, Piece promotionPiece)
        {
            CmdInputMove(playerNetId, startIdx, targetIdx, promotionPiece);
            // Move(startIdx, targetIdx, promotionPiece, NetworkTime.time, netId);
        }

        [Command(requiresAuthority = false)]
        private void CmdInputMove(uint playerNetId, int startIdx, int targetIdx, Piece promotionPiece)
        {
            Debug.Log("[CmdInputMove] playerNetId: " + playerNetId);
            ClientInput input = new ClientInput(ClientInputType.PlayerMoved, netIdToPlayer[playerNetId],
                NetworkTime.time, new MoveInput(startIdx, targetIdx, promotionPiece));
            receivedInputs.Enqueue(input);

            Debug.Log($"Processed CmdInputMove");
            Debug.Log($"Registered input in queue: {input}");
        }

        [Client]
        private void CmdResign(uint playerNetId)
        {
            CmdInputResign(playerNetId);
        }

        [Command(requiresAuthority = false)]
        private void CmdInputResign(uint playerNetId)
        {
            ClientInput input = new ClientInput(ClientInputType.RequestResignation, netIdToPlayer[playerNetId],
                NetworkTime.time,
                null);
            receivedInputs.Enqueue(input);

            Debug.Log($"Processed CmdInputResign");
            Debug.Log($"Registered input in queue: {input}");
        }

        [Client]
        private void CmdOfferDraw(uint playerNetId)
        {
            CmdInputClaimDraw(playerNetId);
        }

        [Command(requiresAuthority = false)]
        private void CmdInputClaimDraw(uint playerNetId)
        {
            ClientInput input = new ClientInput(ClientInputType.RequestDraw, netIdToPlayer[playerNetId],
                NetworkTime.time,
                null);
            receivedInputs.Enqueue(input);

            Debug.Log($"Processed CmdInputClaimDraw");
            Debug.Log($"Registered input in queue: {input}");
        }

        [Server]
        private void StartGame()
        {
            Assert.IsTrue(isServer);
            Assert.IsTrue(NetworkServer.connections.Count == 2);
            Assert.IsTrue(ChessPlayer.GlobalPlayerCount == 2);

            if (NetworkServer.connections.Count != 2 || ChessPlayer.GlobalPlayerCount != 2)
            {
                print($"Server requires exactly two connected players to start" +
                      $", connections: {NetworkServer.connections.Count}" +
                      $", GlobalPlayerCount: {ChessPlayer.GlobalPlayerCount}");
                return;
            }

            var randomColor = Random.Range(0, 1f) < 0.5f ? ChessColor.White : ChessColor.Black;

            ChessPlayer player1ChessPlayer = ChessPlayer.ChessPlayers[0];
            ChessPlayer player2ChessPlayer = ChessPlayer.ChessPlayers[1];

            Debug.Log("[CmdInputMove] player1ChessPlayer.netId: " + player1ChessPlayer.netId);
            Debug.Log("[CmdInputMove] player2ChessPlayer.netId: " + player2ChessPlayer.netId);

            List<NetworkConnection> connections =
                NetworkServer.connections.Select(pair => (NetworkConnection) pair.Value).ToList();

            NetworkConnection player1Connection = connections[0];
            NetworkConnection player2Connection = connections[1];

            player1 = new PlayerData
            {
                networkConnection = player1Connection,
                netId = player1ChessPlayer.netId,
                playerObject = player1ChessPlayer.gameObject,
                chessColor = randomColor,
                timeLeft = START_CLOCK_TIME,
                chessPlayer = player1ChessPlayer,
            };

            netIdToPlayer[player1.netId] = player1;

            player2 = new PlayerData
            {
                networkConnection = player2Connection,
                netId = player2ChessPlayer.netId,
                playerObject = player2ChessPlayer.gameObject,
                chessColor = OpponentColorOf(randomColor),
                timeLeft = START_CLOCK_TIME,
                chessPlayer = player2ChessPlayer,
            };
            netIdToPlayer[player2.netId] = player2;

            if (player1.chessColor == ChessColor.White)
            {
                playerWhite = player1;
                playerBlack = player2;
            }
            else
            {
                playerWhite = player2;
                playerBlack = player1;
            }

            gameStartTime = NetworkTime.time;

            serverCurrentPlayer = playerWhite;

            Debug.Log(player1.chessColor);
            Debug.Log(player2.chessColor);
            Debug.Log(playerWhite.chessColor);
            Debug.Log(playerBlack.chessColor);
            Debug.Log(netIdToPlayer[playerWhite.netId].chessColor);
            Debug.Log(netIdToPlayer[playerBlack.netId].chessColor);

            TRpcStartGame(player1Connection, player1.chessColor, player1.timeLeft, gameStartTime);
            TRpcStartGame(player2Connection, player2.chessColor, player2.timeLeft, gameStartTime);
        }

        #endregion

        #region RpcCalls

        [ClientRpc]
        private void RpcInitGame()
        {
            gameClientState = ClientState.Playing;
        }

        [ClientRpc]
        private void RpcPlayerRequestedDraw(uint playerNetId)
        {
            if (playerNetId == ChessPlayer.PlayerNetID)
            {
                chessboardUI.HideOfferDrawButton();
            }
            else
            {
                chessboardUI.ShowAcceptDrawButton();
                chessboardUI.HideOfferDrawButton();
            }
        }

        [ClientRpc]
        private void RpcMove(uint netId, int startIdx, int targetIdx, Piece promotionPiece,
            double whitePlayerTime, double blackPlayerTime)
        {
            Move(startIdx, targetIdx, promotionPiece, whitePlayerTime, blackPlayerTime);

            timeLeft = localPlayerChessColor == ChessColor.White ? whitePlayerTime : blackPlayerTime;

            isMyTurn = ChessPlayer.PlayerNetID != netId;
        }

        [Client]
        private void Move(int startIdx, int targetIdx, Piece promotionPiece, double whitePlayerTime,
            double blackPlayerTime)
        {
            var currentPlayer = isMyTurn ? localPlayerChessColor : opponentChessColor;
            var nextPlayer = OpponentColorOf(currentPlayer);

            if (isMyTurn)
            {
                chessboardUI.PlayMove(startIdx, targetIdx, promotionPiece);
                OnMoveFinished();
            }
            else
            {
                chessboardUI.StartOpponentMoveAnimation(startIdx, targetIdx, promotionPiece);
                OnMoveFinished();
            }

            m_Clock.SwitchPlayer();
            m_Clock.SetTime(whitePlayerTime, blackPlayerTime);
            chessboardUI.ShowCurrentPlayerIndicator(nextPlayer);

            // Should recognize time-out?
        }

        [TargetRpc]
        private void TRpcStartGame(NetworkConnection conn, ChessColor color, double time, double startTime)
        {
            Debug.Log($"I have the color: {color}");

            localPlayerChessColor = color;
            timeLeft = time;
            opponentChessColor = OpponentColorOf(localPlayerChessColor);

            isMyTurn = localPlayerChessColor == ChessColor.White;

            gameStartTime = startTime;
            chessboardUI.StartGame(localPlayerChessColor);
            hasGameStarted = true;
        }

        private IEnumerator WaitForAnimationToPresentResults(uint? playerNetId, GameResult gameResult)
        {
            while (chessboardUI.IsRunningAnimation())
            {
                yield return null;
            }
            
            gameClientState = ClientState.EndOfGame;

            ChessColor winnerColor = playerNetId == ChessPlayer.PlayerNetID
                ? localPlayerChessColor
                : opponentChessColor;

            switch (gameResult)
            {
                case GameResult.WhiteCheckmated:
                    ShowMessage("Black won by checkmate.");
                    break;
                case GameResult.BlackCheckmated:
                    ShowMessage("White won by checkmate.");
                    break;
                case GameResult.Resignation:
                    ShowGameResult(winnerColor,
                        "White won by resignation.", "Black won by resignation.");
                    break;
                case GameResult.Stalemate:
                    ShowMessage("Stalemate.");
                    break;
                case GameResult.AgreedDraw:
                    ShowMessage("Draw.");
                    break;
                case GameResult.OutOfTime:
                    ShowGameResult(winnerColor,
                        "White won on time.", "Black won on time.");
                    break;
                case GameResult.Walkover:
                    ShowGameResult(winnerColor,
                        "White won. Black left the game.",
                        "Black won. White left the game.");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(gameResult), gameResult, null);
            }
        }
        [ClientRpc]
        private void RpcPresentResultOfGame(uint? playerNetId, GameResult gameResult)
        {
            StartCoroutine(WaitForAnimationToPresentResults(playerNetId, gameResult));
        }

        #endregion

        private void OnMoveFinished()
        {
            chessboardUI.HideAcceptDrawButton();
            chessboardUI.ShowOfferDrawButton();
            m_PendingDrawOfferByOpponent = false;

            chessboardUI.RefreshClaimDrawButton();
            lastConfirmedMoveResult = chessboardUI.LastMoveResult;
        }

        private void StopGame()
        {
            gameIsStopped = true;
            m_Clock.Stop();
            chessboardUI.StopGame();
        }

        [Client]
        private void ShowGameResult(ChessColor winningPlayer, string whiteWinText, string blackWinText)
        {
            switch (winningPlayer)
            {
                case ChessColor.White:
                    ShowMessage(whiteWinText);
                    break;
                case ChessColor.Black:
                    ShowMessage(blackWinText);
                    break;
                case ChessColor.None:
                    ShowMessage("Draw.");
                    break;
            }
        }

        [Server]
        private bool ShouldDumpServerInformation(double previousCallTime)
        {
            return NetworkTime.time > previousCallTime + informationDumpsPerSecond;
        }

        [Server]
        private IEnumerator ServerGameLoop()
        {
            int NumberOfReceivedInputsAccumulator = 0;
            ClientState? previousState = null;
            double previousCallTime = 0;
            WaitForSeconds wait = new WaitForSeconds(0.1f);

            gameServerState = ServerState.WaitingForPlayers;

            Debug.Log("Starting the server game loop");

            while (!gameIsStopped)
            {
                // double timeSinceLastTick = NetworkTime.time - currentGameTime;
                // currentGameTime = NetworkTime.time;
                bool stateJustEntered = gameClientState != previousState;
                previousState = gameClientState;
                ClientInput input;

                PlayerData currentPlayerForThisFrame = serverCurrentPlayer;

                switch (gameServerState)
                {
                    case ServerState.WaitingForPlayers:
                        if (stateJustEntered)
                        {
                            Debug.Log("======================================");
                            Debug.Log("Started waiting for players to join...");
                        }

                        if (ChessPlayer.GlobalPlayerCount == 2 &&
                            NetworkServer.connections.Count == 2 &&
                            NetworkServer.connections
                                .All(conn => conn.Value.isReady))
                        {
                            StartGame();
                            gameServerState = ServerState.WaitingForFirstMove;
                        }

                        if (ShouldDumpServerInformation(previousCallTime))
                        {
                            Debug.Log("Waiting for players to join...");
                            Debug.Log($"{NetworkServer.connections.Count} connection(s) have been established.");
                            previousCallTime = NetworkTime.time;
                        }

                        break;

                    case ServerState.WaitingForFirstMove:
                        if (stateJustEntered)
                        {
                            Debug.Log("=================================");
                            Debug.Log("Started waiting for first move...");
                        }

                        var absoluteAbortTime = gameStartTime + abortTime;
                        NumberOfReceivedInputsAccumulator += receivedInputs.Count;
                        while (receivedInputs.Count != 0)
                        {
                            input = receivedInputs.Dequeue();
                            switch (input.type)
                            {
                                case ClientInputType.PlayerMoved:
                                    MoveInput move = input.move;
                                    if (move == null)
                                    {
                                        throw new ArgumentNullException(
                                            $"Move input field cannot be null for input type {nameof(ClientInputType.PlayerMoved)}");
                                    }

                                    Debug.Log(
                                        $"Logging the inputplayer (netId {input.player.netId}) and the current player (netId {currentPlayerForThisFrame.netId})");
                                    if (input.player == currentPlayerForThisFrame)
                                    {
                                        Debug.Log("Trying the move on serverside...");
                                        if (chessboardUI.Chessboard.TryMove(move.startIdx, move.targetIdx,
                                                move.promotionPiece, out MoveResult result))
                                        {
                                            Debug.Log("Move succeeded!");
                                            if (result == MoveResult.None)
                                            {
                                                double whitePlayerTimeLeft = playerWhite.timeLeft;
                                                double blackPlayerTimeLeft = playerBlack.timeLeft;

                                                Debug.Log("Initiating game...");

                                                RpcInitGame();
                                                RpcMove(currentPlayerForThisFrame.netId, move.startIdx, move.targetIdx,
                                                    move.promotionPiece, whitePlayerTimeLeft, blackPlayerTimeLeft);

                                                Debug.Log(
                                                    $"Calling RpcMove, netId: {currentPlayerForThisFrame.netId} with color: {currentPlayerForThisFrame.chessColor} " +
                                                    $"should move from {move.startIdx} to {move.targetIdx}.");
                                                Debug.Log($"Promotes {move.promotionPiece}.");
                                                Debug.Log($"White has {whitePlayerTimeLeft} time left.");
                                                Debug.Log($"Black has {blackPlayerTimeLeft} time left.");

                                                serverCurrentPlayer = NextPlayer;
                                                gameServerState = ServerState.Playing;
                                                lastMoveTime = NetworkTime.time;
                                            }
                                            else
                                            {
                                                gameServerState = ServerState.Aborted;
                                            }
                                        }
                                    }

                                    break;
                                case ClientInputType.RequestResignation:
                                    Debug.Log("Resignation input received, aborting game...");
                                    gameServerState = ServerState.Aborted;
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }

                        if (NetworkTime.time > absoluteAbortTime)
                        {
                            Debug.Log("Abort timer exceeded, aborting game...");
                            gameServerState = ServerState.Aborted;
                        }
                        else if (ChessPlayer.GlobalPlayerCount != 2)
                        {
                            gameServerState = ServerState.ConnectionError;
                        }

                        if (ShouldDumpServerInformation(previousCallTime))
                        {
                            Debug.Log("Waiting for first move...");
                            Debug.Log(
                                $"Processed {NumberOfReceivedInputsAccumulator} inputs from players during the last {informationDumpsPerSecond} seconds");
                            Debug.Log($"Server abort time is in {absoluteAbortTime - NetworkTime.time} seconds");
                            NumberOfReceivedInputsAccumulator = 0;
                            previousCallTime = NetworkTime.time;
                        }

                        break;

                    case ServerState.Playing:
                        if (stateJustEntered)
                        {
                            Debug.Log("=================================");
                            Debug.Log("Started the game!");
                        }

                        bool resignationCalled = false;
                        uint? resigningPlayerNetId = null;

                        while (receivedInputs.Count != 0)
                        {
                            input = receivedInputs.Dequeue();
                            switch (input.type)
                            {
                                case ClientInputType.PlayerMoved:
                                    MoveInput move = input.move;
                                    if (move == null)
                                    {
                                        throw new ArgumentNullException(
                                            $"Move input field cannot be null for input type {nameof(ClientInputType.PlayerMoved)}");
                                    }

                                    Debug.Log(
                                        $"Logging the inputplayer (netId {input.player.netId}) and the current player (netId {currentPlayerForThisFrame.netId})");
                                    if (input.player == currentPlayerForThisFrame)
                                    {
                                        Debug.Log("Trying the move on serverside...");
                                        if (chessboardUI.Chessboard.TryMove(move.startIdx, move.targetIdx,
                                                move.promotionPiece, out lastConfirmedMoveResult))
                                        {
                                            Debug.Log("Move succeeded!");
                                            double timeElapsedSinceLastMove = NetworkTime.time - lastMoveTime;
                                            currentPlayerForThisFrame.timeLeft -= timeElapsedSinceLastMove;
                                            double whitePlayerTimeLeft = playerWhite.timeLeft;
                                            double blackPlayerTimeLeft = playerBlack.timeLeft;
                                            RpcMove(currentPlayerForThisFrame.netId, move.startIdx, move.targetIdx,
                                                move.promotionPiece, whitePlayerTimeLeft, blackPlayerTimeLeft);

                                            Debug.Log(
                                                $"Calling RpcMove, netId: {serverCurrentPlayer.netId} with color: {serverCurrentPlayer.chessColor} " +
                                                $"should move from {move.startIdx} to {move.targetIdx}.");
                                            Debug.Log($"Promotes {move.promotionPiece}.");
                                            Debug.Log($"White has {whitePlayerTimeLeft} time left.");
                                            Debug.Log($"Black has {blackPlayerTimeLeft} time left.");

                                            serverCurrentPlayer = NextPlayer;
                                            lastMoveTime = NetworkTime.time;

                                            playerWhite.isRequestingDraw = false;
                                            playerBlack.isRequestingDraw = false;
                                        }
                                    }

                                    break;

                                case ClientInputType.RequestDraw:
                                    Debug.Log($"{input.player.chessColor} requested a draw...");
                                    input.player.isRequestingDraw = true;
                                    if (!player1.isRequestingDraw || !player2.isRequestingDraw)
                                    {
                                        RpcPlayerRequestedDraw(input.player.netId);
                                    }

                                    break;

                                case ClientInputType.RequestResignation:
                                    Debug.Log($"{input.player.chessColor} requested a resignation...");
                                    resignationCalled = true;
                                    resigningPlayerNetId = input.player.netId;
                                    break;

                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }

                        bool timeout = currentPlayerForThisFrame.timeLeft < NetworkTime.time - lastMoveTime;

                        if (lastConfirmedMoveResult != MoveResult.None)
                        {
                            Debug.Log($"The previous move resulted in {lastConfirmedMoveResult}");

                            switch (lastConfirmedMoveResult)
                            {
                                case MoveResult.WhiteCheckmated:
                                    RpcPresentResultOfGame(currentPlayerForThisFrame.netId,
                                        GameResult.WhiteCheckmated);

                                    // Register checkmate (win) in profile, and in ChallengerMode (TODO)
                                    gameServerState = ServerState.EndOfGame;
                                    break;
                                case MoveResult.BlackCheckmated:
                                    RpcPresentResultOfGame(currentPlayerForThisFrame.netId,
                                        GameResult.BlackCheckmated);

                                    // Register checkmate (win) in profile, and in ChallengerMode (TODO)
                                    gameServerState = ServerState.EndOfGame;
                                    break;
                                case MoveResult.StaleMate:
                                    RpcPresentResultOfGame(null,
                                        GameResult.Stalemate);

                                    // Register stalemate (draw) in profile, and in ChallengerMode (TODO)
                                    gameServerState = ServerState.EndOfGame;
                                    break;
                            }

                            gameServerState = ServerState.EndOfGame;
                        }
                        else if (resignationCalled)
                        {
                            if (!resigningPlayerNetId.HasValue)
                            {
                                throw new Exception(
                                    "Could not find a value in resigning player net id, despite having set boolean that resignation was called");
                            }

                            Debug.Log(
                                $"Player {NetIdToColor(resigningPlayerNetId.Value)} (netId: {resigningPlayerNetId.Value}) resigned");

                            var resigningPlayerData = netIdToPlayer[resigningPlayerNetId.Value];
                            RpcPresentResultOfGame(OpponentPlayer(resigningPlayerData).netId,
                                GameResult.Resignation);

                            // Register resignation (loss) in profile, and in ChallengerMode (TODO)
                            gameServerState = ServerState.EndOfGame;
                        }
                        else if (timeout)
                        {
                            RpcPresentResultOfGame(OpponentPlayer(currentPlayerForThisFrame).netId,
                                GameResult.OutOfTime);

                            Debug.Log(
                                $"Player {currentPlayerForThisFrame.chessColor} (netId: {currentPlayerForThisFrame.netId}) timed out");

                            // Register timeout (loss) in profile, and in ChallengerMode (TODO)
                            gameServerState = ServerState.EndOfGame;
                        }
                        else if (ChessPlayer.GlobalPlayerCount == 1)
                        {
                            ChessPlayer existingPlayer = ChessPlayer.ChessPlayers[0];
                            RpcPresentResultOfGame(existingPlayer.netId, GameResult.Walkover);

                            Debug.Log(
                                $"Player {NetIdToColor(existingPlayer.netId)} (netId: {existingPlayer.netId}) won from opponent disconnecting");

                            // Register walkover (win) in profile, and in ChallengerMode (TODO)
                            gameServerState = ServerState.EndOfGame;
                        }
                        else if (player1.isRequestingDraw && player2.isRequestingDraw)
                        {
                            RpcPresentResultOfGame(null,
                                GameResult.AgreedDraw);

                            Debug.Log($"A draw was agreed upon.");

                            // Register draw (draw) in profile, and in ChallengerMode (TODO)
                            gameServerState = ServerState.EndOfGame;
                        }

                        break;

                    case ServerState.Aborted:
                        RpcPresentResultOfGame(null, GameResult.Aborted);
                        StopGame();
                        Debug.Log($"Aborted game.");
                        yield break;

                    case ServerState.EndOfGame:
                        StopGame();
                        Debug.Log($"Ended game.");
                        yield break;

                    case ServerState.ConnectionError:
                        StopGame();
                        Debug.Log($"Lost connection to player(s) during first move stage, so aborted game.");
                        yield break;
                }

                yield return wait;
            }

            yield return new WaitForSeconds(afterGameLobbyCloseTime);
            
            if (isServerOnly)
            {
#if !UNITY_EDITOR
                NetworkServer.DisconnectAll();
                roomNetworkManager.StopRoomServer();
#endif
            }
            else
            {
                NetworkClient.Disconnect();
            }
        }


        [Client]
        private IEnumerator ClientGameLoop()
        {
            var wait = new WaitForSeconds(0.1f);
            bool timeOutRpcCalled = false;
            int retryCount = 0;
            ClientState? previousState = null;

            Debug.Log("Starting the client");

            while (true)
            {
                bool stateJustEntered = gameClientState != previousState;
                previousState = gameClientState;
                switch (gameClientState)
                {
                    case ClientState.Connecting:
                        if (stateJustEntered)
                        {
                            ShowMessage("Connecting...");
                            chessboardUI.SetResignButtonActive(false);
                            chessboardUI.SetNewOpponentButtonActive(false);
                            chessboardUI.ShowLoadingIndicator();
                            chessboardUI.HideOfferDrawButton();
                            chessboardUI.HideAcceptDrawButton();
                        }

                        if (NetworkClient.isConnected)
                        {
                            gameClientState = ClientState.WaitingForOpponent;
                        }

                        break;

                    case ClientState.WaitingForOpponent:
                        if (stateJustEntered)
                        {
                            ShowMessage("Waiting for an opponent...");
                        }

                        if (hasGameStarted)
                        {
                            gameClientState = ClientState.WaitingForFirstMove;
                        }
                        else if (!NetworkClient.isConnected)
                        {
                            gameClientState = ClientState.ConnectionError;
                        }

                        break;

                    case ClientState.WaitingForFirstMove:
                        if (stateJustEntered)
                        {
                            chessboardUI.HideLoadingIndicator();
                            chessboardUI.ShowTime(ChessColor.White, timeLeft);
                            chessboardUI.ShowTime(ChessColor.Black, timeLeft);
                            chessboardUI.ShowCurrentPlayerIndicator(ChessColor.White);
                        }

                        double clientAbortTime = gameStartTime + abortTime;
                        float secondsLeft = Mathf.Ceil((float) (clientAbortTime - NetworkTime.time));

                        string msg = localPlayerChessColor == ChessColor.Black ? "White has" : "You have";

                        ShowMessage(secondsLeft <= -5
                            ? $"You have probably lost connection or gotten out of sync..."
                            : $"{msg} {Mathf.Clamp(secondsLeft, 0, abortTime)} seconds to play the first move");

                        if (!NetworkClient.isConnected)
                        {
                            gameClientState = ClientState.ConnectionError;
                        }

                        break;
                    case ClientState.Playing:
                        if (stateJustEntered)
                        {
                            chessboardUI.SetResignButtonActive(true);
                            HideMessage();
                            chessboardUI.ShowOfferDrawButton();
                        }

                        ChessColor color = isMyTurn ? localPlayerChessColor : opponentChessColor;

                        double currentPlayerTime = m_Clock.GetTime(color);
                        chessboardUI.ShowTime(color, currentPlayerTime);

                        break;
                    case ClientState.Aborted:
                        if (stateJustEntered)
                        {
                            ShowMessage("The game was aborted.");
                            StopGame();
                        }

                        break;

                    case ClientState.EndOfGame:
                        if (stateJustEntered)
                        {
                            StopGame();
                        }

                        break;

                    case ClientState.ConnectionError:
                        if (stateJustEntered)
                        {
                            ShowMessage("Connection error. Try again.");
                            StopGame();
                        }

                        break;
                }

                yield return wait;
            }
        }

        [Client]
        private void ShowMessage(string text)
        {
            chessboardUI.ShowMessage(text);
        }

        [Client]
        private void HideMessage()
        {
            chessboardUI.HideMessage();
        }

        private enum GameResult
        {
            WhiteCheckmated,
            BlackCheckmated,
            Resignation,
            Stalemate,
            AgreedDraw,
            OutOfTime,
            Walkover,
            Aborted
        }

        private enum ServerState
        {
            WaitingForFirstMove,
            Playing,
            EndOfGame,
            Aborted,
            ConnectionError,
            WaitingForPlayers
        }

        private enum ClientState
        {
            Connecting,
            WaitingForOpponent,
            WaitingForFirstMove,
            Playing,
            EndOfGame,
            Aborted,
            ConnectionError,
        }

        private class ClientInput
        {
            public ClientInputType type;
            public PlayerData player;
            public double inputReceivedTimeStamp;
            public MoveInput move;

            public ClientInput(ClientInputType type, PlayerData player, double inputReceivedTimeStamp, MoveInput move)
            {
                this.type = type;
                this.player = player;
                this.inputReceivedTimeStamp = inputReceivedTimeStamp;
                this.move = move;
            }

            public override string ToString()
            {
                return
                    $"type: {type}, player: {player}, " +
                    $"inputReceivedTimeStamp: {inputReceivedTimeStamp}, move: {move}";
            }
        }

        private class PlayerData
        {
            // Generic data
            public NetworkConnection networkConnection;
            public uint netId;
            public GameObject playerObject;

            // Chess specific data
            public ChessColor chessColor;
            public double timeLeft;
            public ChessPlayer chessPlayer;
            public bool isRequestingDraw;

            public override string ToString()
            {
                return
                    $"networkConnection: {networkConnection}, netId: {netId}, playerObject: {playerObject}, chessColor: {chessColor}, " +
                    $"timeLeft: {timeLeft}, chessPlayer: {chessPlayer}, isRequestingDraw: {isRequestingDraw}";
            }
        }

        private enum ClientInputType
        {
            PlayerMoved,
            RequestResignation,
            RequestDraw,
        }

        private class MoveInput
        {
            public int startIdx;
            public int targetIdx;
            public Piece promotionPiece;

            public MoveInput(int startIdx, int targetIdx, Piece promotionPiece)
            {
                this.startIdx = startIdx;
                this.targetIdx = targetIdx;
                this.promotionPiece = promotionPiece;
            }

            public override string ToString()
            {
                return
                    $"startIdx: {startIdx}, targetIdx: {targetIdx}, " +
                    $"promotionPiece: {promotionPiece}";
            }
        }
    }
}