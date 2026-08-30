using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PredictedPlayerController : NetworkBehaviour
{
	[SerializeField]
	private Rigidbody rigidbody;
	[SerializeField]
	private float mouseRotationPerUnit = 2.0f;
	[SerializeField]
	private float mouseRotationDrag = 2.0f;

	[SerializeField]
	private float keyboardRotationAcceleration = 45.0f;
	[SerializeField]
	private float keyboardRotationBrakeAcceleration = 180.0f;
	[SerializeField]
	private float keyboardRotationMaxSpeed = 360.0f;

	[SerializeField]
	private float movementAcceleration = 2.2f;
	[SerializeField]
	private float brakeAcceleration = 8.7f;
	[SerializeField]
	private float movementMaxVelocity = 8.7f;

	[SerializeField]
	private Transform playerModel = null;
	[SerializeField]
	private Transform playerCamera = null;

	private TextMeshProUGUI velocityDisplay = null;
	private Vector3 currentThrustInput = Vector3.zero;
	private bool isBraking = false;

	private Vector3 rotationVelocity = Vector3.zero;
	private Vector2 mouseRotationVelocity = Vector2.zero;

	[SerializeField]
	InputActionReference LookInput;
	[SerializeField]
	InputActionReference MoveInput;
	[SerializeField]
	InputActionReference UpDownInput;
	[SerializeField]
	InputActionReference RollInput;
	[SerializeField]
	InputActionReference BrakeInput;
	[SerializeField]
	InputActionReference PrimaryInput;
	[SerializeField]
	InputActionReference SecondaryInput;

	[SerializeField]
	private List<Tool> tools = new List<Tool>();
	private int currentTool = 0;
	private NetworkVariable<Vector3> serverPosition = new();
	private NetworkVariable<Quaternion> serverRotation = new();
	private NetworkVariable<Vector3> serverLinearVelocity = new();

	[SerializeField]
	private VisualDecoupler decoupler;

	private Vector3 latestServerThrust = Vector3.zero;
	private bool latestServerBraking = false;
	private Quaternion latestServerRotation = Quaternion.identity;

	private float rpcTimer = 0f;
	private const float RpcSendInterval = 0.05f;


	private void OnPrimaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool].PressPrimary();
	private void OnPrimaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool].ReleasePrimary();
	private void OnSecondaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool].PressSecondary();
	private void OnSecondaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool].ReleaseSecondary();
	private void OnBrakeInputPerformed(InputAction.CallbackContext context) => isBraking = true;
	private void OnBrakeInputCanceled(InputAction.CallbackContext context) => isBraking = false;

	public override void OnNetworkSpawn()
	{
		if (IsOwner)
		{
			playerCamera.gameObject.SetActive(true);
			playerModel.gameObject.SetActive(false);

			BrakeInput.action.performed += OnBrakeInputPerformed;
			BrakeInput.action.canceled += OnBrakeInputCanceled;
			PrimaryInput.action.performed += OnPrimaryInputPerformed;
			PrimaryInput.action.canceled += OnPrimaryInputCanceled;
			SecondaryInput.action.performed += OnSecondaryInputPerformed;
			SecondaryInput.action.canceled += OnSecondaryInputCanceled;

			Cursor.lockState = CursorLockMode.Locked;

			velocityDisplay = FindAnyObjectByType<UIVelocity>().GetComponent<TextMeshProUGUI>();

			Vector2 spawnPoint = UnityEngine.Random.insideUnitCircle.normalized * 3;
			transform.position = transform.position + new Vector3(spawnPoint.x, 0.0f, spawnPoint.y);
		}
		else if (IsServer)
		{
			latestServerRotation = transform.rotation;
		}
		else if (!IsServer) {
			rigidbody.isKinematic = true;
		}
	}

	public override void OnNetworkDespawn()
	{
		if (IsOwner) {
			BrakeInput.action.performed -= OnBrakeInputPerformed;
			BrakeInput.action.canceled -= OnBrakeInputCanceled;
			PrimaryInput.action.performed -= OnPrimaryInputPerformed;
			PrimaryInput.action.canceled -= OnPrimaryInputCanceled;
			SecondaryInput.action.performed -= OnSecondaryInputPerformed;
			SecondaryInput.action.canceled -= OnSecondaryInputCanceled;
		}

		Destroy(decoupler.gameObject);
	}

	private void Update()
	{
		if (!IsOwner) return;

		if (!isBraking)
		{
			currentThrustInput = new Vector3(MoveInput.action.ReadValue<Vector2>().x, UpDownInput.action.ReadValue<float>(), MoveInput.action.ReadValue<Vector2>().y) * movementAcceleration;
		}
		else
		{
			currentThrustInput = Vector3.zero;
		}

		float NewRollVelocity = rotationVelocity.z;
		float RollInputValue = RollInput.action.ReadValue<float>();

		if (RollInputValue != 0.0f)
		{
			NewRollVelocity += RollInputValue * keyboardRotationAcceleration * Time.deltaTime;
		}
		else
		{
			NewRollVelocity -= Mathf.Clamp(keyboardRotationBrakeAcceleration * Time.deltaTime * Mathf.Sign(NewRollVelocity), NewRollVelocity * -Mathf.Sign(NewRollVelocity), NewRollVelocity * Mathf.Sign(NewRollVelocity));
		}

		rotationVelocity.z = Mathf.Clamp(NewRollVelocity, -keyboardRotationMaxSpeed, keyboardRotationMaxSpeed);
		mouseRotationVelocity += new Vector2(LookInput.action.ReadValue<Vector2>().x, -LookInput.action.ReadValue<Vector2>().y) * mouseRotationPerUnit;
		mouseRotationVelocity = Vector2.Lerp(mouseRotationVelocity, Vector2.zero, mouseRotationDrag * Time.deltaTime);

		transform.Rotate(Vector3.up, mouseRotationVelocity.x);
		transform.Rotate(Vector3.right, mouseRotationVelocity.y);
		transform.Rotate(Vector3.forward, rotationVelocity.z * Mathf.Deg2Rad);

		// UI Stuff
		if (velocityDisplay != null)
		{
			velocityDisplay.text = string.Format("{0:0.##} M/S", rigidbody.linearVelocity.magnitude);
		}
	}

	private void FixedUpdate()
	{
		if (IsOwner)
		{
			ApplyPhysicsLogic(currentThrustInput, isBraking);

			if (!IsServer)
			{
				rpcTimer += Time.fixedDeltaTime;
				if (rpcTimer >= RpcSendInterval)
				{
					SendInputServerRpc(currentThrustInput, isBraking, rigidbody.rotation);
					rpcTimer = 0.0f;
				}

				rigidbody.position = Vector3.Lerp(rigidbody.position, serverPosition.Value, 0.1f);
				rigidbody.linearVelocity = Vector3.Lerp(rigidbody.linearVelocity, serverLinearVelocity.Value, 0.1f);
			}
		}
		else if (IsServer)
		{
			rigidbody.rotation = Quaternion.Slerp(rigidbody.rotation, latestServerRotation, 15f * Time.fixedDeltaTime);

			ApplyPhysicsLogic(latestServerThrust, latestServerBraking);
		}
		else
		{
			rigidbody.position = Vector3.Lerp(rigidbody.position, serverPosition.Value, 15f * Time.fixedDeltaTime);
			rigidbody.rotation = Quaternion.Slerp(rigidbody.rotation, serverRotation.Value, 15f * Time.fixedDeltaTime);
			rigidbody.linearVelocity = serverLinearVelocity.Value;
		}

		if (IsServer)
		{
			serverPosition.Value = rigidbody.position;
			serverRotation.Value = rigidbody.rotation;
			serverLinearVelocity.Value = rigidbody.linearVelocity;
		}
	}

	[Rpc(SendTo.Server)]
	private void SendInputServerRpc(Vector3 thrustInput, bool braking, Quaternion clientRotation)
	{
		latestServerThrust = thrustInput;
		latestServerBraking = braking;
		latestServerRotation = clientRotation;
	}

	private void ApplyPhysicsLogic(Vector3 thrustInput, bool braking)
	{
		Vector3 appliedVelocity;

		if (braking)
		{
			appliedVelocity = Vector3.ClampMagnitude(-transform.InverseTransformDirection(rigidbody.linearVelocity.normalized) * brakeAcceleration * Time.fixedDeltaTime, rigidbody.linearVelocity.magnitude);
		}
		else
		{
			appliedVelocity = thrustInput * Time.fixedDeltaTime;
		}

		Vector3 NewVelocity = rigidbody.linearVelocity + transform.TransformDirection(appliedVelocity);

		if (NewVelocity.magnitude <= movementMaxVelocity)
		{
			if (rigidbody.IsSleeping())
			{
				rigidbody.WakeUp();
			}

			rigidbody.linearVelocity = NewVelocity;
		}
	}
}
