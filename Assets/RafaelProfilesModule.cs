using MasterServerToolkit.MasterServer;
using MasterServerToolkit.Networking;
using MasterServerToolkit.Utils;
using UnityEngine;

namespace Rafael.Tutorials
{
    public enum ObservablePropertiyCodes
    {
        DisplayName,
        Avatar,
        Bronze,
        Silver,
        Gold
    }

    public class RafaelProfilesModule : ProfilesModule
    {
        [Header("Start Values"), SerializeField]
        private float bronze = 100;
        [SerializeField]
        private float silver = 50;
        [SerializeField]
        private float gold = 50;
        [SerializeField]
        private string avatarUrl = "https://i.imgur.com/JQ9pRoD.png";

        public override void Initialize(IServer server)
        {
            base.Initialize(server);

            // Set the new factory in ProfilesModule
            ProfileFactory = CreateProfileInServer;
        }
        
        private ObservableServerProfile CreateProfileInServer(string username, IPeer clientPeer)
        {
            return new ObservableServerProfile(username, clientPeer)
            {
                new ObservableString((short)ObservablePropertiyCodes.DisplayName, SimpleNameGenerator.Generate(Gender.Male)),
                new ObservableString((short)ObservablePropertiyCodes.Avatar, avatarUrl),
                new ObservableFloat((short)ObservablePropertiyCodes.Bronze, bronze),
                new ObservableFloat((short)ObservablePropertiyCodes.Silver, silver),
                new ObservableFloat((short)ObservablePropertiyCodes.Gold, gold),
            };
        }
    }
}