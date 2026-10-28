using FaceSlapper.Network.Prediction;
using FaceSlapper.VFX;
using UnityEngine;

public class PlayerVFXView : MonoBehaviour
{
    [SerializeField] MeteorAtmosphereVFX _meteorVFX;
    private PlayerControllerPrediction _playerController;
    private void Awake()
    {
        _playerController = GetComponent<PlayerControllerPrediction>();
    }

    private void Update()
    {
        if(_playerController.State.IsCharging && !_meteorVFX.gameObject.activeSelf)
        {
            _meteorVFX.gameObject.SetActive(true);
            _meteorVFX.Play();
        }
        if(!_playerController.State.IsCharging && _meteorVFX.gameObject.activeSelf)
        {
            _meteorVFX.Stop();
            _meteorVFX.gameObject.SetActive(false);
        }
    }
}
