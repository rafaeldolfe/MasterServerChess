using System;
using System.Collections;
using System.Collections.Generic;
using MasterServerToolkit.MasterServer;
using MasterServerToolkit.Networking.Unet;
using UnityEngine;

public class RafaelServerListTutorial : MonoBehaviour
{
    private RoomController roomController;
        private RoomOptions roomOptions;

        // Start is called before the first frame update
        void Start()
        {
            SetRoomOptions();

            Mst.Client.Connection.AddConnectionListener(OnConnectedToMasterServerHandler, true);
            Mst.Client.Connection.AddDisconnectionListener(OnDisconnectedFromMasterServerHandler, false);
        }

        private void SetRoomOptions()
        {
            roomOptions = new RoomOptions
            {
                IsPublic = true,
                // Your Game Server Name
                Name = "My Game With Friends",
                // If you want your server to be passworded
                Password = "",
                // Machine IP the server is running on
                RoomIp = "127.0.0.1",
                // Port of the game server
                RoomPort = 7777,
                // The max number of connections
                MaxConnections = 10
            };
        }

        private void OnConnectedToMasterServerHandler()
        {
            // If this room was spawned
            if (Mst.Server.Spawners.IsSpawnedProccess)
            {
                // Try to register spawned process first
                RegisterSpawnedProcess();
            }
            else
            {
                RegisterRoom();
            }
        }

        private void RegisterRoom()
        {
            Mst.Server.Rooms.RegisterRoom(roomOptions, (controller, error) =>
            {
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError(error);
                    return;
                }

                roomController = controller;

                Debug.Log("Our server was successfully registered");
            });
        }

        private void RegisterSpawnedProcess()
        {
            // Let's register this process
            Mst.Server.Spawners.RegisterSpawnedProcess(Mst.Args.SpawnTaskId, Mst.Args.SpawnTaskUniqueCode, (taskController, error) =>
            {
                if (taskController == null)
                {
                    Debug.LogError($"Room server process cannot be registered. The reason is: {error}");
                    return;
                }

                // If max players was given from spawner task
                if (taskController.Options.Has(MstDictKeys.ROOM_NAME))
                {
                    roomOptions.Name = taskController.Options.AsString(MstDictKeys.ROOM_NAME);
                }

                // If room is public or not
                if (taskController.Options.Has(MstDictKeys.ROOM_IS_PUBLIC))
                {
                    roomOptions.IsPublic = taskController.Options.AsBool(MstDictKeys.ROOM_IS_PUBLIC);
                }

                // If max players param was given from spawner task
                if (taskController.Options.Has(MstDictKeys.ROOM_MAX_CONNECTIONS))
                {
                    roomOptions.MaxConnections = taskController.Options.AsInt(MstDictKeys.ROOM_MAX_CONNECTIONS);
                }

                // If password was given from spawner task
                if (taskController.Options.Has(MstDictKeys.ROOM_PASSWORD))
                {
                    roomOptions.Password = taskController.Options.AsString(MstDictKeys.ROOM_PASSWORD);
                }

                // Finalize spawn task before we start server
                taskController.FinalizeTask(new MstProperties(), () =>
                {
                    RegisterRoom();
                });
            });
        }

        private void OnDisconnectedFromMasterServerHandler()
        {
            Mst.Server.Rooms.DestroyRoom(roomController.RoomId, (isSuccess, error) =>
            {
                // Your code here...
            });
        }
}
