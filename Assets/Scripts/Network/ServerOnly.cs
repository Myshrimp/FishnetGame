using FaceSlapper.Network.Prediction;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Network.Prediction
{
    public class ServerOnly : NetworkBehaviour
    {
        private PlayerControllerPrediction.PlayerState playerState;
        private bool isServer;
        public override void OnStartServer()
        {
            base.OnStartServer();
            playerState = GetComponent<PlayerControllerPrediction>().State;
            isServer = true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isServer) return;
            if(collision.collider.GetComponent<PlayerControllerPrediction>() != null)
            {

            }
        }
    }
}

