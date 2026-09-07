using UnityEngine;

public class ToolCutter : Tool
{
	[SerializeField]
	private int cutStrength = 1;

	[Header("Evaporate")]
	[SerializeField]
	private float maxEvaporateDistance = 10.0f;

	[SerializeField]
	private float evaporateSpherecastRadius = 0.25f;

	private Transform playerCamera;

	protected override void OnNetworkPostSpawn()
	{
		if (!IsOwner) return;

		playerCamera = transform.parent.parent;
	}

	public override void PressPrimary()
	{
		//TODO Cut object in half baby!
	}

	public override void PressSecondary()
	{
		Debug.Log("PressSecondaryCutter");
		
		//TODO Evaporate Object babyyy
		if (!IsOwner) return;

		Debug.Log("TryGetLookedAtSpaceshipPart");

		if (TryGetLookedAtSpaceshipPart(out SpaceshipPart spaceshipPart))
		{
			Debug.Log("Found Part");

			// EvaporatePart handles the client -> server routing and despawn itself.
			spaceshipPart.EvaporatePart();
		}
	}

	private bool TryGetLookedAtSpaceshipPart(out SpaceshipPart spaceshipPart)
	{
		spaceshipPart = null;

		RaycastHit[] hits = Physics.SphereCastAll(playerCamera.position, evaporateSpherecastRadius, playerCamera.forward, maxEvaporateDistance);
		System.Array.Sort(hits, delegate (RaycastHit x, RaycastHit y) { return x.distance.CompareTo(y.distance); });

		foreach (RaycastHit hit in hits)
		{
			if (hit.transform.gameObject == this.NetworkObject.transform.gameObject)
				continue;

			if (hit.collider.TryGetComponent(out SpaceshipPart part))
			{
				spaceshipPart = part;
				return true;
			}
		}

		return false;
	}

	public override void PressTertiary()
	{
		//TODO Rotate cutting angle 90 degrees
	}

	public override void ReleasePrimary()
	{
		
	}

	public override void ReleaseSecondary()
	{
		
	}

	public override void ReleaseTertiary()
	{
		
	}
}
