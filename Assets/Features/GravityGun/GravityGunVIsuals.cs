using System;
using DG.Tweening;
using UnityEngine;

public class GravityGunVisuals : MonoBehaviour
{
    [SerializeField] private ToolGravitygun _toolGravitygun;
    [SerializeField] private Transform _chargeVisualsParent;
    [SerializeField] private ParticleSystem _shootParticles;
    [SerializeField] private Vector3 _punch;
    [SerializeField] private float _duration;
    [SerializeField] private int _vibrato = 10;
    [SerializeField] private float _elasticity = 1F;

    private Quaternion _defaultRotation;

    private void Start()
    {
        _defaultRotation = transform.localRotation;
        _toolGravitygun.OnShootEvent += OnShoot;
    }

    private void OnDestroy()
    {
        _toolGravitygun.OnShootEvent -= OnShoot;
    }
    
    private void OnShoot(float charge01)
    {
        transform.localRotation = _defaultRotation;
        transform.DOPunchRotation(_punch * charge01, _duration, _vibrato, _elasticity);
        _shootParticles.Play();
    }

    private void Update()
    {
        EnableChargeVisuals();
    }

    private void EnableChargeVisuals()
    {
        float threshold = (1.0f / _chargeVisualsParent.childCount) - 0.001f;
        for (int i = 0; i < _chargeVisualsParent.childCount; i++)
        {
            bool shouldBeEnabled = _toolGravitygun.CurrentCharge01 > (threshold * i);
            _chargeVisualsParent.GetChild(i).gameObject.SetActive(shouldBeEnabled);
        }
    }
}
