using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;
public class GameSensor : NetworkBehaviour
{
    public static List<GameObject> PlayerGOs;
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (PlayerGOs == null) PlayerGOs = new List<GameObject>();
        PlayerGOs.Add(this.gameObject);
    }
}
