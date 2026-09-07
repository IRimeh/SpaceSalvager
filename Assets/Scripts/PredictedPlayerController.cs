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

	// Networked so remote clients also see which tool is active (and toggle the correct model).
	// Owner writes it (via the scroll wheel); everyone reads it.
	private NetworkVariable<int> currentTool = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

	[SerializeField]
	private float toolSwitchScrollThreshold = 0.01f;

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
	private void OnPrimaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool.Value].PressPrimary();
	private void OnPrimaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool.Value].ReleasePrimary();
	private void OnSecondaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool.Value].PressSecondary();
	private void OnSecondaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool.Value].ReleaseSecondary();
	private void OnTertiaryInputPerformed(InputAction.CallbackContext context) => tools[currentTool.Value].PressTertiary();
	private void OnTertiaryInputCanceled(InputAction.CallbackContext context) => tools[currentTool.Value].ReleaseTertiary();
	private void OnBrakeInputPerformed(InputAction.CallbackContext context) => isBraking = true;
	private void OnBrakeInputCanceled(InputAction.CallbackContext context) => isBraking = false;
	#endregion

	#region Tool Switching
	// Owner-only: reads the mouse scroll wheel and cycles the active tool index.
	private void HandleToolSwitchInput()
	{
		if (tools.Count <= 1) return;
		if (Mouse.current == null) return;

		float scroll = Mouse.current.scroll.ReadValue().y;
		if (Mathf.Abs(scroll) < toolSwitchScrollThreshold) return;

		int direction = scroll > 0f ? 1 : -1;
		int count = tools.Count;
		// Wrap around in both directions.
		int newIndex = ((currentTool.Value + direction) % count + count) % count;

		if (newIndex != currentTool.Value)
		{
			// Owner has write permission; this replicates and triggers OnValueChanged everywhere.
			currentTool.Value = newIndex;
		}
	}

	// Runs on every peer (via the NetworkVariable callback) so the correct tool model is shown for all.
	private void OnCurrentToolChanged(int previousIndex, int newIndex)
	{
		ApplyToolActiveStates(newIndex);
	}

	private void ApplyToolActiveStates(int activeIndex)
	{
		for (int i = 0; i < tools.Count; i++)
		{
			if (tools[i] == null) continue;

			bool shouldBeActive = (i == activeIndex);
			if (tools[i].gameObject.activeSelf != shouldBeActive)
			{
				tools[i].gameObject.SetActive(shouldBeActive);
			}
		}
	}
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
		// Everyone (owner, server, remote clients) keeps the correct tool model enabled.
		currentTool.OnValueChanged += OnCurrentToolChanged;
		ApplyToolActiveStates(currentTool.Value);

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
		currentTool.OnValueChanged -= OnCurrentToolChanged;

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

		HandleToolSwitchInput();

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

		//calculate new Mouse Rotation Velocity and Interpolate it for schmooseness
		Vector2 LookInputValue = LookInput.action.ReadValue<Vector2>();
		mouseRotationVelocity += new Vector2(LookInputValue.x, -LookInputValue.y) * mouseRotationPerUnit;
		mouseRotationVelocity *= Mathf.Exp(-mouseRotationDrag * Time.deltaTime);

		//Make it Framerate independed
		Vector3 rotationThisFrame = new Vector3(
			mouseRotationVelocity.y,
			mouseRotationVelocity.x,
			rotationVelocity.z
			) * Time.deltaTime;

		//Rotate dat Ass
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

			//Apply the grappling hook force locally for client prediction
			rigidbody.AddForce(currentContinuousForce, ForceMode.Force);

			if (!IsServer)
			{
				//Only Send Position and Force Updates in a fixed interval 
				rpcTimer += Time.fixedDeltaTime;
				if (rpcTimer >= RpcSendInterval)
				{
					SendInputServerRpc(currentThrustInput, isBraking, rigidbody.rotation, currentContinuousForce);
					rpcTimer = 0.0f;
				}

				//Smoothly apply servervalues
				rigidbody.position = Vector3.Lerp(rigidbody.position, serverPosition.Value, 0.1f);
				rigidbody.linearVelocity = Vector3.Lerp(rigidbody.linearVelocity, serverLinearVelocity.Value, 0.1f);
			}
		}
		else if (IsServer)
		{
			rigidbody.rotation = Quaternion.Slerp(rigidbody.rotation, latestServerRotation, 15f * Time.fixedDeltaTime);

			ApplyPhysicsLogic(latestServerThrust, latestServerBraking);

			//Server applies the synced grappling hook force authoritatively 
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
		latestServerExternalForce = externalForce;
	}


	/// <summary>
	/// Applies Movement Input Velocity to the Rigidbody. Should only be called in FixedUpdate.
	/// </summary>
	/// <param name="thrustInput"></param>
	/// <param name="braking"></param>
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

		//Wake up the rigidbody, because applying velocity manuelly does not do that
		if (rigidbody.IsSleeping())
		{
			rigidbody.WakeUp();
		}

		//Apply the velocity baby
		rigidbody.linearVelocity = newVelocity;
	}
}
