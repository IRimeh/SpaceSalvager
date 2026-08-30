using System;
using UnityEngine;

public class EnableOnConnect : MonoBehaviour
{
	[SerializeField] private GameObject _gameObjectToEnable;
	
	private void Awake()
	{
		UGSManager.OnConnect += OnConnect;
	}

	private void OnDestroy()
	{
		UGSManager.OnConnect -= OnConnect;
	}

	private void OnConnect()
	{
		_gameObjectToEnable.SetActive(true);
	}
}
