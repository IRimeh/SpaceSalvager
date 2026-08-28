using Unity.Netcode;

public class DisableOnConnect : NetworkBehaviour
{
    protected override void OnNetworkPostSpawn()
    {
        base.OnNetworkPostSpawn();
        gameObject.SetActive(false);
    }
}
