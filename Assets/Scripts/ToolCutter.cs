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

	[SerializeField]
	private Transform playerCamera;

	[Tooltip("Seconds between heat heartbeats sent to the aimed part while secondary is held.")]
	[SerializeField]
	private float contributionSendInterval = 0.1f;

	private bool isHeating;
	private float lastContributionTime;


	public override void PressPrimary()
	{
		//TODO Cut object in half baby!
	}

	public override void PressSecondary()
	{
		if (!IsOwner) return;

		// Begin heating: hold to heat the aimed part until it evaporates. Send an
		// immediate heartbeat so heating starts without waiting for the interval.
		isHeating = true;
		lastContributionTime = float.NegativeInfinity;
		SendHeatContribution();
	}

	private void Update()
	{
		if (!IsOwner || !isHeating) return;

		if (Time.time - lastContributionTime >= contributionSendInterval)
		{
			SendHeatContribution();
		}
	}

	private void SendHeatContribution()
	{
		lastContributionTime = Time.time;

		if (TryGetLookedAtSpaceshipPart(out SpaceshipPart spaceshipPart))
		{
			// Server accumulates heat from all contributors; it decides when to evaporate.
			spaceshipPart.HeatContributionServerRpc(cutStrength);
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
		if (!IsOwner) return;

		// Stop sending heartbeats. The server times out this contributor shortly
		// after, and the part cools down if no other cutter is heating it.
		isHeating = false;
	}

	public override void ReleaseTertiary()
	{
		
	}
}
