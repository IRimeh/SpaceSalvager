using UnityEngine;

public class VisualDecoupler : MonoBehaviour
{
	[SerializeField]
	private Transform networkRoot;

	[SerializeField]
	private float smoothTime = .05f;

	[SerializeField]
	private float rotationLerpSpeed = 20f;

	private Vector3 positionVelocity = Vector3.zero;

	private void Start()
	{
		transform.parent = null;
	}

	private void LateUpdate()
	{
		transform.position = Vector3.SmoothDamp(
			transform.position,
			networkRoot.position,
			ref positionVelocity,
			smoothTime);

		// 3. Smoothly chase the rotation
		transform.rotation = Quaternion.Slerp(
			transform.rotation,
			networkRoot.rotation,
			Time.deltaTime * rotationLerpSpeed);
	}
}
