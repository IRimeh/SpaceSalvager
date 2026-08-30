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
		//What if root is destroyed?
		
		transform.SetPositionAndRotation(Vector3.SmoothDamp(
			transform.position,
			networkRoot.position,
			ref positionVelocity,
			smoothTime), Quaternion.Slerp(
			transform.rotation,
			networkRoot.rotation,
			Time.deltaTime * rotationLerpSpeed));
	}

	
}
