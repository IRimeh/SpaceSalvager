using System;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : NetworkBehaviour
{
	[SerializeField]
	private float mouseRotationPerUnit = 2.0f;

	//This is the time it needs to reach the desired curser position
	[SerializeField]
	private float mouseRotationDrag = 2.0f;

	//TODO When the mouse is moved fast it moves further


	[SerializeField] //In degrees/second
	private float keyboardRotationAcceleration = 45.0f;

	[SerializeField] //In degrees/second
	private float keyboardRotationBrakeAcceleration = 180.0f;

	[SerializeField] //In degrees/second
	private float keyboardRotationMaxSpeed = 360.0f;


	[SerializeField] //In meters/second
	private float movementAcceleration = 2.2f;

	[SerializeField] //In meters/second
	private float brakeAcceleration = 8.7f;

	[SerializeField] //In meters/second
	private float movementMaxVelocity = 8.7f;

	[SerializeField]
	private Transform playerModel = null;

	[SerializeField]
	private Transform playerCamera = null;

	private TextMeshProUGUI velocityDisplay = null;

	private NetworkRigidbody networkRigidbody = null;

	private Vector3 inputVelocity = Vector3.zero;

	private bool isBraking = false;

	//In degrees/second (x = pitch, y = yaw, z = roll)
	private Vector3 rotationVelocity = Vector3.zero;


	private Vector2 mouseRotationVelocity = Vector2.zero;

	//TODO Gotta add audiolistener

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


	protected override void OnNetworkPostSpawn()
	{
		if (IsOwner)
		{
			playerCamera.gameObject.SetActive(true);
			playerModel.gameObject.SetActive(false);
		}
	}

	private void Start()
	{
		networkRigidbody = GetComponent<NetworkRigidbody>();

		BrakeInput.action.performed += OnBrakeInputPerformed;
		BrakeInput.action.canceled += OnBrakeInputCanceled;

		//Cursor.lockState = CursorLockMode.Locked;

		//velocityDisplay = FindObjectOfType<UIVelocity>().GetComponent<TextMeshProUGUI>();
	}

	private void OnBrakeInputCanceled(InputAction.CallbackContext context)
	{
		isBraking = false;
	}

	private void OnBrakeInputPerformed(InputAction.CallbackContext context)
	{
		isBraking = true;
	}

	private void Update()
	{
		if (!IsOwner) { return; }

		if (!isBraking)
		{
			inputVelocity = new Vector3(MoveInput.action.ReadValue<Vector2>().x, UpDownInput.action.ReadValue<float>(), MoveInput.action.ReadValue<Vector2>().y) * movementAcceleration;
		}

		//Compute Keyboard Rotation
		float NewRollVelocity = rotationVelocity.z;

		float RollInputValue = RollInput.action.ReadValue<float>();

		if (RollInputValue != 0.0f)
		{
			NewRollVelocity += RollInputValue * keyboardRotationAcceleration * Time.deltaTime;
			Debug.Log(NewRollVelocity);
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

		//UI Stuff
		//velocityDisplay.text = string.Format("{0:0.##} M/S", playerRigidbody.linearVelocity.magnitude);
	}

	private void FixedUpdate()
	{
		if (!IsOwner) { return; }

		if (isBraking)
		{
			//TODO Maybe use drag to brake?
			//inputVelocity = Vector3.ClampMagnitude(-transform.InverseTransformDirection(playerRigidbody.linearVelocity.normalized) * brakeAcceleration * Time.fixedDeltaTime, playerRigidbody.linearVelocity.magnitude);
			inputVelocity = Vector3.ClampMagnitude(-transform.InverseTransformDirection(networkRigidbody.GetLinearVelocity().normalized) * brakeAcceleration * Time.fixedDeltaTime, networkRigidbody.GetLinearVelocity().magnitude);
		}
		else
		{
			inputVelocity = inputVelocity * Time.fixedDeltaTime;
		}

		Vector3 NewVelocity = networkRigidbody.GetLinearVelocity() + transform.TransformDirection(inputVelocity);

		Debug.Log(NewVelocity);

		if (NewVelocity.magnitude <= movementMaxVelocity)
		{
			//playerRigidbody.linearVelocity = NewVelocity;
			networkRigidbody.SetLinearVelocity(NewVelocity);
		}
	}
}
