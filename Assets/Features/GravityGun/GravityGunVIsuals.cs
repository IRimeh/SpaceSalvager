using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class GravityGunVisuals : MonoBehaviour
{
    [SerializeField] private ToolGravitygun _toolGravitygun;
    [SerializeField] private Transform _chargeVisualsParent;
    [SerializeField] private ParticleSystem _shootParticles;
    [SerializeField] private Vector3 _punch;
    [SerializeField] private float _duration;
    [SerializeField] private int _vibrato = 10;
    [SerializeField] private float _elasticity = 1F;

    [Header("Decal")] 
    [SerializeField] private DecalProjector _decalProjector;

    private Vector3 _defaultDecalSize;
    private Quaternion _defaultRotation;

    private void Start()
    {
        _defaultDecalSize = _decalProjector.size;
        _decalProjector.size = Vector3.zero;
        _defaultRotation = transform.localRotation;
        _toolGravitygun.OnShootEvent += OnShoot;
        _toolGravitygun.OnStartHoldingEvent += OnStartHolding;
        _toolGravitygun.OnStopHoldingEvent += OnStopHolding;
    }

    private void OnDestroy()
    {
        _toolGravitygun.OnShootEvent -= OnShoot;
        _toolGravitygun.OnStartHoldingEvent -= OnStartHolding;
        _toolGravitygun.OnStopHoldingEvent -= OnStopHolding;
    }
    
    private void OnStartHolding(Interactable interactable)
    {
        _decalProjector.size = _defaultDecalSize;
    }
    
    private void OnStopHolding(Interactable interactable)
    {
        _decalProjector.size = Vector3.zero;
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

    private void LateUpdate()
    {
        GrabDecal();
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

    private void GrabDecal()
    {
        if (!_toolGravitygun.IsHolding)
            return;

        _decalProjector.transform.position = _toolGravitygun.GrabbedRigidbody.transform.TransformPoint(_toolGravitygun.LocalGrabOffset);
        _decalProjector.transform.forward = _toolGravitygun.GrabbedRigidbody.transform.TransformDirection(_toolGravitygun.LocalGrabNormal);
    }
}
