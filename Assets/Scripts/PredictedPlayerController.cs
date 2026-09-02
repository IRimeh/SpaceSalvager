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
	InputActionReference TertiaryInput;

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
	private void OnTertiaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool].PressTertiary();
	private void OnTertiaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool].ReleaseTertiary();
	private void OnBrakeInputPerformed(InputAction.CallbackContext context) => isBraking = true;
	private void OnBrakeInputCanceled(InputAction.CallbackContext context) => isBraking = false;

	// NEW: Helper properties for the Gravity Gun
	public float Mass => rigidbody.mass;
	public Vector3 LinearVelocity => rigidbody.linearVelocity;

	// NEW: Force tracking
	private Vector3 currentContinuousForce = Vector3.zero;
	private Vector3 latestServerExternalForce = Vector3.zero;

	// NEW: Store continuous force locally (sent to server in batches)
	public void SetContinuousForce(Vector3 force)
	{
		currentContinuousForce = force;
	}

	// NEW: One-off impulse trigger for the Hookshot launch
	public void ApplyImpulse(Vector3 force)
	{
		if (IsOwner)
		{
			rigidbody.AddForce(force, ForceMode.Impulse);
			ApplyImpulseServerRpc(force);
		}
	}

	[Rpc(SendTo.Server)]
	private void ApplyImpulseServerRpc(Vector3 force)
	{
		rigidbody.AddForce(force, ForceMode.Impulse);
	}

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
			TertiaryInput.action.performed += OnTertiaryInputPerformed;
			TertiaryInput.action.canceled += OnTertiaryInputCanceled;

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

		//TODO Mouselook is kinda framerate dependend I think
		//transform.Rotate(Vector3.up, mouseRotationVelocity.x * Time.deltaTime);
		//transform.Rotate(Vector3.right, mouseRotationVelocity.y * Time.deltaTime);
		//transform.Rotate(Vector3.forward, rotationVelocity.z * Time.deltaTime);

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

			// NEW: Apply the grappling hook force locally for client prediction
			rigidbody.AddForce(currentContinuousForce, ForceMode.Force);

			if (!IsServer)
			{
				rpcTimer += Time.fixedDeltaTime;
				if (rpcTimer >= RpcSendInterval)
				{
					// UPDATED: Now passes the continuous force to the server
					SendInputServerRpc(currentThrustInput, isBraking, rigidbody.rotation, currentContinuousForce);
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

			// NEW: Server applies the synced grappling hook force authoritatively 
			rigidbody.AddForce(latestServerExternalForce, ForceMode.Force);
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
	private void SendInputServerRpc(Vector3 thrustInput, bool braking, Quaternion clientRotation, Vector3 externalForce) // UPDATED
	{
		latestServerThrust = thrustInput;
		latestServerBraking = braking;
		latestServerRotation = clientRotation;
		latestServerExternalForce = externalForce; // NEW
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

		Vector3 newVelocity = rigidbody.linearVelocity + transform.TransformDirection(appliedVelocity);

		// Get the player's current speed
		float currentSpeed = rigidbody.linearVelocity.magnitude;

		// The maximum allowed speed is either the default max, or the current over-max speed
		float maxAllowedSpeed = Mathf.Max(movementMaxVelocity, currentSpeed);

		// Clamp the new velocity so the player cannot accelerate past maxAllowedSpeed,
		// but they CAN still brake (lowering magnitude) and steer (changing direction).
		if (newVelocity.magnitude > maxAllowedSpeed)
		{
			newVelocity = Vector3.ClampMagnitude(newVelocity, maxAllowedSpeed);
		}

		if (rigidbody.IsSleeping())
		{
			rigidbody.WakeUp();
		}

		rigidbody.linearVelocity = newVelocity;
	}
}
