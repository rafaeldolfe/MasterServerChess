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
using System.Diagnostics.Tracing;
using System.Timers;
using kcp2k;
using Unity.VectorGraphics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Chessticle
{
    public class ChessboardUI : MonoBehaviour
    {
        public Sprite[] PieceSprites;
        public Chessboard Chessboard;
        public SVGImage PieceTemplate;
        public GameObject MessageTextParent;
        public RectTransform BoardRect;
        public Text TopTimeText;
        public Text BottomTimeText;
        public Button ResignButton;
        public Button NewOpponentButton;
        public Button ClaimDrawButton;
        public Image CheckIndicatorImage;
        public GameObject PromotionUI;
        public GameObject LoadingIndicator;
        public Button OfferDrawButton;
        public Button AcceptDrawButton;


        public delegate void MoveClientInputCallback(uint playerNetId, int startIdx, int targetIdx,
            Piece promotionPiece);

        public delegate void GenericClientInputCallback(uint playerNetId);

        public event Action CmdOpponentMoveFinished;
        public event GenericClientInputCallback CmdResignationRequested;
        public event Action CmdNewOpponentRequested;
        public event GenericClientInputCallback CmdClaimDrawRequested;
        public event GenericClientInputCallback CmdOfferDrawRequested;
        public event MoveClientInputCallback CmdPlayerMoved;

        private Piece m_PromotionPiece;
        private Text m_WhiteTimeText;
        private Text m_BlackTimeText;
        private Text m_MessageText;
        private int m_MoveStartRank;
        private int m_MoveStartFile;
        private ChessColor _mLocalPlayerChessColor;
        private Vector2 m_SquareSize;

        private readonly Dictionary<Piece, PieceImagePool> m_ImagePoolsByPieceWhite =
            new Dictionary<Piece, PieceImagePool>();

        private readonly Dictionary<Piece, PieceImagePool> m_ImagePoolsByPieceBlack =
            new Dictionary<Piece, PieceImagePool>();

        private Task opponentAnimation;
        private Task moveSuccessClientPrediction;
        private bool ignoreMoveInput;
        [SerializeField] private float maxTimeForServerResponse = 1f;

        private void Start()
        {
            HidePromotionUI();
            m_MessageText = MessageTextParent.GetComponentInChildren<Text>();
            m_SquareSize = BoardRect.rect.size / 8;
            var pieceSize = m_SquareSize * 0.7f;
            CheckIndicatorImage.GetComponent<RectTransform>().sizeDelta = m_SquareSize;
            PieceTemplate.GetComponent<RectTransform>().sizeDelta = pieceSize;

            foreach (var sprite in PieceSprites)
            {
                var color = sprite.name[0] == 'w' ? ChessColor.White : ChessColor.Black;
                var piece = Chessboard.CharToPiece(sprite.name[1], color);
                var dict = color == ChessColor.White ? m_ImagePoolsByPieceWhite : m_ImagePoolsByPieceBlack;
                if (!dict.ContainsKey(piece))
                {
                    dict[piece] = new PieceImagePool(PieceTemplate, sprite, piece);
                }
            }

            Destroy(PieceTemplate.gameObject);
            Refresh();

            SetDraggingEnabled(ChessColor.White, false);
            SetDraggingEnabled(ChessColor.Black, false);
        }

        private (int rank, int file) PointerPositionToBoardCoords(Camera eventCamera, Vector2 eventPosition)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(BoardRect, eventPosition,
                    eventCamera, out var position))
            {
                var boardSize = BoardRect.rect.size;
                var pos01 = (position + boardSize / 2) / boardSize;
                var rank = (int) Mathf.Floor(pos01.y * 8);
                var file = (int) Mathf.Floor(pos01.x * 8);
                if (_mLocalPlayerChessColor == ChessColor.White)
                {
                    rank = 7 - rank;
                }
                else
                {
                    file = 7 - file;
                }

                return (rank, file);
            }

            Assert.IsTrue(false);
            return (-1, -1);
        }

        public void OnStartMove(PointerEventData data)
        {
            (m_MoveStartRank, m_MoveStartFile)
                = PointerPositionToBoardCoords(data.pressEventCamera, data.pressPosition);
        }

        public void OnEndMove(PointerEventData data)
        {
            if (Chessboard.CurrentPlayer != _mLocalPlayerChessColor)
            {
                Refresh();
                return;
            }
            (int endRank, int endFile) = PointerPositionToBoardCoords(data.pressEventCamera, data.position);
            StartCoroutine(SendLocalMoveToServer(m_MoveStartRank, m_MoveStartFile, endRank, endFile));
        }


        public void OnPromotionPieceSelected(Piece piece)
        {
            m_PromotionPiece = piece;
        }

        private IEnumerator SendLocalMoveToServer(int startRank, int startFile, int targetRank, int targetFile)
        {
            if (ignoreMoveInput)
            {
                yield break;
            }
            var startIdx = Chessboard.CoordsToIndex0X88(startRank, startFile);
            var targetIdx = Chessboard.CoordsToIndex0X88(targetRank, targetFile);
            if (IsLegalMove(startIdx, targetIdx))
            {
                var promotionPiece = Piece.None;
                if (Chessboard.IsPromotingMove(startIdx, targetIdx))
                {
                    ShowPromotionUI();
                    m_PromotionPiece = Piece.None;
                    while (m_PromotionPiece == Piece.None)
                    {
                        yield return null;
                    }

                    promotionPiece = m_PromotionPiece;
                    HidePromotionUI();
                }

                moveSuccessClientPrediction = Task.Get(WaitForServerToHandleMoveInput());

                CmdPlayerMoved?.Invoke(ChessPlayer.PlayerNetID, startIdx, targetIdx, promotionPiece);
            }
            else
            {
                Refresh();
            }

        }

        private IEnumerator WaitForServerToHandleMoveInput()
        {
            ChessColor previousPlayer = Chessboard.CurrentPlayer;
            float clock = 0;
            ignoreMoveInput = true;
            
            while (Chessboard.CurrentPlayer == previousPlayer && clock < maxTimeForServerResponse)
            {
                clock += Time.deltaTime;
                yield return null;
            }

            ignoreMoveInput = false;
            
            Refresh();
        }

        private bool IsLegalMove(int startIdx, int targetIdx)
        {
            return Chessboard.IsLegalMove(startIdx, targetIdx);
        }

        public void OfferDrawButton_Click()
        {
            CmdOfferDrawRequested?.Invoke(ChessPlayer.PlayerNetID);
        }

        public void ResignButton_Click()
        {
            CmdResignationRequested?.Invoke(ChessPlayer.PlayerNetID);
        }

        public void NewOpponentButton_Click()
        {
            CmdNewOpponentRequested?.Invoke();
        }

        public void ClaimDraw_Click()
        {
            CmdClaimDrawRequested?.Invoke(ChessPlayer.PlayerNetID);
        }

        public void ShowAcceptDrawButton()
        {
            AcceptDrawButton.gameObject.SetActive(true);
        }

        public void HideAcceptDrawButton()
        {
            AcceptDrawButton.gameObject.SetActive(false);
        }

        public void UndoLastMove()
        {
            Chessboard.UndoLastMove();
            Refresh();
        }

        public void StartGame(ChessColor localPlayerChessColor)
        {
            if (localPlayerChessColor == ChessColor.White)
            {
                m_WhiteTimeText = BottomTimeText;
                m_BlackTimeText = TopTimeText;
            }
            else
            {
                m_WhiteTimeText = TopTimeText;
                m_BlackTimeText = BottomTimeText;
            }

            _mLocalPlayerChessColor = localPlayerChessColor;
            SetDraggingEnabled(ChessColor.White, localPlayerChessColor == ChessColor.White);
            SetDraggingEnabled(ChessColor.Black, false);

            Refresh();
        }

        public void StartOpponentMoveAnimation(int startIdx, int targetIdx, Piece promotionPiece)
        {
            opponentAnimation = Task.Get(DoAnimateOpponentMove(startIdx, targetIdx, promotionPiece));
        }

        private IEnumerator DoAnimateOpponentMove(int startIdx, int targetIdx, Piece promotionPiece)
        {
            var (piece, color) = Chessboard.GetPiece(startIdx);
            bool virgin = Chessboard.IsVirgin(startIdx);
            Chessboard.SetPiece(startIdx, Piece.None, ChessColor.None, false);
            Refresh();

            var pools = color == ChessColor.White ? m_ImagePoolsByPieceWhite : m_ImagePoolsByPieceBlack;
            var pool = pools[piece];
            var image = pool.GetImage();
            image.transform.SetAsLastSibling();

            var (startRank, startFile) = Chessboard.Index0X88ToCoords(startIdx);
            var (targetRank, targetFile) = Chessboard.Index0X88ToCoords(targetIdx);

            Vector2 startPos;
            Vector2 targetPos;
            if (color == ChessColor.White)
            {
                startPos = GetAnchoredPosition(startRank, 7 - startFile);
                targetPos = GetAnchoredPosition(targetRank, 7 - targetFile);
            }
            else
            {
                startPos = GetAnchoredPosition(7 - startRank, startFile);
                targetPos = GetAnchoredPosition(7 - targetRank, targetFile);
            }

            float t = 0;
            while (t < 1f)
            {
                t += Time.deltaTime * 5f;
                var pos = Vector2.Lerp(startPos, targetPos, t);
                image.rectTransform.anchoredPosition = pos;
                yield return null;
            }

            image.rectTransform.anchoredPosition = targetPos;

            Chessboard.SetPiece(startIdx, piece, color, virgin);
            bool didMove = TryMove(startIdx, targetIdx, promotionPiece);
            Assert.IsTrue(didMove);
            Refresh();
            CmdOpponentMoveFinished?.Invoke();
        }

        public MoveResult LastMoveResult { private set; get; }

        public void SetResignButtonActive(bool active)
        {
            ResignButton.gameObject.SetActive(active);
        }

        public void SetNewOpponentButtonActive(bool active)
        {
            NewOpponentButton.gameObject.SetActive(active);
        }

        public void ShowMessage(string message)
        {
            m_MessageText.text = message;
            MessageTextParent.SetActive(true);
        }

        public void HideMessage()
        {
            MessageTextParent.SetActive(false);
        }

        public void ShowLoadingIndicator()
        {
            LoadingIndicator.SetActive(true);
        }

        public void HideLoadingIndicator()
        {
            LoadingIndicator.SetActive(false);
        }

        public void ShowTime(ChessColor chessColor, double timeSeconds)
        {
            var span = TimeSpan.FromSeconds(timeSeconds);
            var format = timeSeconds < 60 ? @"mm\:ss\.f" : @"mm\:ss";
            var time = span.ToString(format);
            if (chessColor == ChessColor.White)
            {
                m_WhiteTimeText.text = time;
            }
            else
            {
                m_BlackTimeText.text = time;
            }
        }

        public void ShowCurrentPlayerIndicator(ChessColor chessColor)
        {
            m_WhiteTimeText.GetComponentInChildren<Image>(true).enabled = chessColor == ChessColor.White;
            m_BlackTimeText.GetComponentInChildren<Image>(true).enabled = chessColor == ChessColor.Black;
        }

        private bool TryMove(int startIdx, int targetIdx, Piece promotionPiece)
        {
            bool didMove = Chessboard.TryMove(startIdx, targetIdx, promotionPiece, out var result);
            if (didMove)
            {
                SetDraggingEnabled(_mLocalPlayerChessColor,
                    Chessboard.CurrentPlayer == _mLocalPlayerChessColor);
                LastMoveResult = result;
            }

            return didMove;
        }

        public ChessColor CurrentPlayer => Chessboard.CurrentPlayer;

        private void Refresh()
        {
            HideAll();

            CheckIndicatorImage.gameObject.SetActive(false);
            var (checkRank, checkFile) = Chessboard.GetCheckCoords();
            if (_mLocalPlayerChessColor == ChessColor.White)
            {
                checkRank = 7 - checkRank;
            }
            else
            {
                checkFile = 7 - checkFile;
            }

            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    if (rank == checkRank && file == checkFile)
                    {
                        CheckIndicatorImage.rectTransform.anchoredPosition = GetAnchoredPosition(rank, file);
                        CheckIndicatorImage.gameObject.SetActive(true);
                    }

                    int r = _mLocalPlayerChessColor == ChessColor.White ? 7 - rank : rank;
                    int f = _mLocalPlayerChessColor == ChessColor.White ? file : 7 - file;

                    var (piece, color) = Chessboard.GetPiece(r, f);
                    if (piece == Piece.None)
                    {
                        continue;
                    }

                    var pool = color == ChessColor.White
                        ? m_ImagePoolsByPieceWhite[piece]
                        : m_ImagePoolsByPieceBlack[piece];

                    var image = pool.GetImage();
                    image.rectTransform.anchoredPosition = GetAnchoredPosition(rank, file);
                }
            }
        }

        private Vector2 GetAnchoredPosition(int rank, int file)
        {
            return new Vector2((file + 0.5f) * m_SquareSize.x, (rank + 0.5f) * m_SquareSize.y);
        }

        private void HideAll()
        {
            foreach (var piece in m_ImagePoolsByPieceWhite.Keys)
            {
                m_ImagePoolsByPieceWhite[piece].HideAll();
            }

            foreach (var piece in m_ImagePoolsByPieceBlack.Keys)
            {
                m_ImagePoolsByPieceBlack[piece].HideAll();
            }
        }

        public void RefreshClaimDrawButton()
        {
            ClaimDrawButton.gameObject.SetActive(Chessboard.CanDrawBeClaimed);
        }

        private void HideClaimDrawButton()
        {
            ClaimDrawButton.gameObject.SetActive(false);
        }

        public void ShowOfferDrawButton()
        {
            OfferDrawButton.gameObject.SetActive(true);
        }

        public void HideOfferDrawButton()
        {
            OfferDrawButton.gameObject.SetActive(false);
        }

        public void StopGame()
        {
            StopAllCoroutines();
            SetDraggingEnabled(ChessColor.White, false);
            SetDraggingEnabled(ChessColor.Black, false);
            HidePromotionUI();
            SetResignButtonActive(false);
            SetNewOpponentButtonActive(false);
            RefreshClaimDrawButton();
            HideLoadingIndicator();
            HideClaimDrawButton();
            HideAcceptDrawButton();
            HideOfferDrawButton();
        }

        private void HidePromotionUI()
        {
            PromotionUI.SetActive(false);
        }

        private void ShowPromotionUI()
        {
            PromotionUI.transform.SetAsLastSibling(); // move to front
            PromotionUI.SetActive(true);
        }

        private void SetDraggingEnabled(ChessColor chessColor, bool draggingEnabled)
        {
            var pools = chessColor == ChessColor.White ? m_ImagePoolsByPieceWhite : m_ImagePoolsByPieceBlack;
            foreach (var key in pools.Keys)
            {
                var pool = pools[key];
                pool.SetDraggingEnabled(draggingEnabled);
            }
        }

        public void PlayMove(int startIdx, int targetIdx, Piece promotionPiece)
        {
            bool triedMoveGivenByServer = Chessboard.TryMove(startIdx, targetIdx, promotionPiece, out _);
            if (!triedMoveGivenByServer)
            {
                // In the future, request the server to sync the whole board
                throw new Exception($"Server and client mismatch - client cannot play move demanded by server");
            }
            else
            {
                Refresh();
                Debug.Log("Successfully played move demanded by server");
            }
        }

        public bool IsRunningAnimation()
        {
            return opponentAnimation is {Running: true};
        }
    }
}