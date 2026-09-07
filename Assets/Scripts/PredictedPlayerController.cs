using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using NaughtyAttributes;

public class PredictedPlayerController : NetworkBehaviour
{
	#region References
	[Foldout("References")]
	[SerializeField]
	private Rigidbody rigidbody;

	[Foldout("References")]
	[SerializeField]
	private Transform playerModel = null;

	[Foldout("References")]
	[SerializeField]
	private Transform playerCamera = null;

	[Foldout("References")]
	[SerializeField]
	private VisualDecoupler decoupler;

	[Foldout("References")]
	[SerializeField]
	private List<Tool> tools = new List<Tool>();

	[Foldout("References")]
	[SerializeField]
	InputActionReference LookInput;

	[Foldout("References")]
	[SerializeField]
	InputActionReference MoveInput;

	[Foldout("References")]
	[SerializeField]
	InputActionReference UpDownInput;

	[Foldout("References")]
	[SerializeField]
	InputActionReference RollInput;

	[Foldout("References")]
	[SerializeField]
	InputActionReference BrakeInput;

	[Foldout("References")]
	[SerializeField]
	InputActionReference PrimaryInput;

	[Foldout("References")]
	[SerializeField]
	InputActionReference SecondaryInput;

	[Foldout("References")]
	[SerializeField]
	InputActionReference TertiaryInput;

	private TextMeshProUGUI velocityDisplay = null;
	#endregion

	#region Input Variables
	[SerializeField]
	private float mouseRotationPerUnit = 0.2f;
	[SerializeField]
	private float mouseRotationDrag = 3f;

	[SerializeField]
	private float keyboardRotationAcceleration = 40f;
	[SerializeField]
	private float keyboardRotationBrakeAcceleration = 80f;
	[SerializeField]
	private float keyboardRotationMaxSpeed = 160f;

	[SerializeField]
	private float movementAcceleration = 2.2f;
	[SerializeField]
	private float brakeAcceleration = 8.7f;
	[SerializeField]
	private float movementMaxVelocity = 8.7f;
	#endregion

	private int currentTool = 0;

	private Vector3 currentThrustInput = Vector3.zero;
	private bool isBraking = false;
	private Vector3 rotationVelocity = Vector3.zero;
	private Vector2 mouseRotationVelocity = Vector2.zero;
	private NetworkVariable<Vector3> serverPosition = new();
	private NetworkVariable<Quaternion> serverRotation = new();
	private NetworkVariable<Vector3> serverLinearVelocity = new();
	private Vector3 latestServerThrust = Vector3.zero;
	private bool latestServerBraking = false;
	private Quaternion latestServerRotation = Quaternion.identity;
	public float Mass => rigidbody.mass;
	public Vector3 LinearVelocity => rigidbody.linearVelocity;
	private Vector3 currentContinuousForce = Vector3.zero;
	private Vector3 latestServerExternalForce = Vector3.zero;

	#region RPC Timer Variables
	private float rpcTimer = 0f;
	private const float RpcSendInterval = 0.05f;
	#endregion

	#region Input Callbacks
	private void OnPrimaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool].PressPrimary();
	private void OnPrimaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool].ReleasePrimary();
	private void OnSecondaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool].PressSecondary();
	private void OnSecondaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool].ReleaseSecondary();
	private void OnTertiaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool].PressTertiary();
	private void OnTertiaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool].ReleaseTertiary();
	private void OnBrakeInputPerformed(InputAction.CallbackContext context) => isBraking = true;
	private void OnBrakeInputCanceled(InputAction.CallbackContext context) => isBraking = false;
	#endregion

	// NEW: Store continuous force locally (sent to server in batches)
	public void SetContinuousForce(Vector3 force)
	{
		currentContinuousForce = force;
	}

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

			//TODO Temporary. Add actuall Spawnpoint logic
			Vector2 spawnPoint = UnityEngine.Random.insideUnitCircle.normalized * 3;
			transform.position = transform.position + new Vector3(spawnPoint.x, 0.0f, spawnPoint.y);
		}
		else if (IsServer)
		{
			latestServerRotation = transform.rotation;
		}
		else if (!IsServer)
		{
			rigidbody.isKinematic = true;
		}
	}

	public override void OnNetworkDespawn()
	{
		if (IsOwner)
		{
			BrakeInput.action.performed -= OnBrakeInputPerformed;
			BrakeInput.action.canceled -= OnBrakeInputCanceled;
			PrimaryInput.action.performed -= OnPrimaryInputPerformed;
			PrimaryInput.action.canceled -= OnPrimaryInputCanceled;
			SecondaryInput.action.performed -= OnSecondaryInputPerformed;
			SecondaryInput.action.canceled -= OnSecondaryInputCanceled;
			TertiaryInput.action.performed -= OnTertiaryInputPerformed;
			TertiaryInput.action.canceled -= OnTertiaryInputCanceled;
		}

		Destroy(decoupler.gameObject);
	}

	private void Update()
	{
		if (!IsOwner) return;

		//Get Thrustinput if not braking
		currentThrustInput = Vector3.zero;

		if (!isBraking)
		{
			Vector2 MoveInputValue = MoveInput.action.ReadValue<Vector2>();
			float UpDownInputValue = UpDownInput.action.ReadValue<float>();
			currentThrustInput = new Vector3(MoveInputValue.x, UpDownInputValue, MoveInputValue.y) * movementAcceleration;
		}


		//Calculate Input or Damper depending on if the player presses the roll key
		float NewRollVelocity = rotationVelocity.z;
		float RollInputValue = RollInput.action.ReadValue<float>();

		if (RollInputValue != 0.0f)
		{
			NewRollVelocity += RollInputValue * keyboardRotationAcceleration * Time.deltaTime;
		}
		else
		{
			//Calculate Deceleration and make sure it doesnt overshoot 0
			float NewRollVelocitySign = Mathf.Sign(NewRollVelocity);
			NewRollVelocity -= Mathf.Clamp(keyboardRotationBrakeAcceleration * NewRollVelocitySign * Time.deltaTime, NewRollVelocity * -NewRollVelocitySign, NewRollVelocity * NewRollVelocitySign);
		}

		//Apply Roll Velocity and clamp it to max speed
		rotationVelocity.z = Mathf.Clamp(NewRollVelocity, -keyboardRotationMaxSpeed, keyboardRotationMaxSpeed);


		Vector2 LookInputValue = LookInput.action.ReadValue<Vector2>();
		mouseRotationVelocity += new Vector2(LookInputValue.x, -LookInputValue.y) * mouseRotationPerUnit;
		mouseRotationVelocity *= Mathf.Exp(-mouseRotationDrag * Time.deltaTime);


		Vector3 rotationThisFrame = new Vector3(
			mouseRotationVelocity.y,
			mouseRotationVelocity.x,
			rotationVelocity.z
			) * Time.deltaTime;

		transform.Rotate(rotationThisFrame, Space.Self);

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
