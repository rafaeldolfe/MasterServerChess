using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class ChessPlayer : NetworkBehaviour
{
    public static int GlobalPlayerCount = 0;
    public static List<ChessPlayer> ChessPlayers = new List<ChessPlayer>();
    private static uint? playerNetID = null;
    public static uint PlayerNetID
    {
        get
        {
            if (!playerNetID.HasValue)
            {
                throw new ArgumentNullException($"playerNetID accessed before being set");
            }

            return playerNetID.Value;
        }
        set => playerNetID = value;
    }

    public override void OnStartClient()
    {
        if (isLocalPlayer)
        {
            PlayerNetID = netId;
        }
    }

    private void Awake()
    {
        GlobalPlayerCount++;
        ChessPlayers.Add(this);
    }
 
    private void OnDestroy()
    {
        GlobalPlayerCount--;
        ChessPlayers.Remove(this);
    }
 
    private void Start()
    {
        if (isLocalPlayer)
        {
            transform.name = $"{nameof(ChessPlayer)} (Local,netId={netId})";
        }
        else
            transform.name = $"{nameof(ChessPlayer)} (Network, netId={netId})";
 
        print($"{transform.name}, total players: {GlobalPlayerCount}");
    }
}