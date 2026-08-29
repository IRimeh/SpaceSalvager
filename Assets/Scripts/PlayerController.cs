using NUnit.Framework;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;

public class PlayerController : NetworkBehaviour
{
	//TODO When the mouse is moved fast it moves further
	
	[SerializeField] 
	private Rigidbody rigidbody;
	[SerializeField]
	private float mouseRotationPerUnit = 2.0f;
	[SerializeField] //This is the time it needs to reach the desired curser position
	private float mouseRotationDrag = 2.0f;

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
	private Vector3 inputVelocity = Vector3.zero;
	private bool isBraking = false;
	
	private Vector3 rotationVelocity = Vector3.zero; //In degrees/second (x = pitch, y = yaw, z = roll)
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

	protected override void OnNetworkPostSpawn()
	{
		if (!IsOwner)
			return;
		
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
	}

	private void OnPrimaryInputPerformed(InputAction.CallbackContext context)
	{
		tools[currentTool].PressPrimary();
	}

	private void OnPrimaryInputCanceled(InputAction.CallbackContext context)
	{
		tools[currentTool].ReleasePrimary();
	}

	private void OnSecondaryInputPerformed(InputAction.CallbackContext context)
	{
		tools[currentTool].PressSecondary();
	}

	private void OnSecondaryInputCanceled(InputAction.CallbackContext context)
	{
		tools[currentTool].ReleaseSecondary();
	}

	public override void OnNetworkDespawn()
	{
		BrakeInput.action.performed -= OnBrakeInputPerformed;
		BrakeInput.action.canceled -= OnBrakeInputCanceled;
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
		velocityDisplay.text = string.Format("{0:0.##} M/S", rigidbody.linearVelocity.magnitude);
	}

	private void FixedUpdate()
	{
		if (!IsOwner) { return; }

		if (isBraking)
		{
			//TODO Maybe use drag to brake?
			inputVelocity = Vector3.ClampMagnitude(-transform.InverseTransformDirection(rigidbody.linearVelocity.normalized) * brakeAcceleration * Time.fixedDeltaTime, rigidbody.linearVelocity.magnitude);
		}
		else
		{
			inputVelocity *= Time.fixedDeltaTime;
		}
		
		Vector3 NewVelocity = rigidbody.linearVelocity + transform.TransformDirection(inputVelocity);
		if (NewVelocity.magnitude <= movementMaxVelocity)
		{
			rigidbody.linearVelocity = NewVelocity;
		}
	}
}
