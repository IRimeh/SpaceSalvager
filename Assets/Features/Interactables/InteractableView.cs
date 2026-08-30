using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Interactable))]
public class InteractableView : MonoBehaviour
{
	[SerializeField] private Interactable _interactable;
	[SerializeField] private MeshRenderer _meshRenderer;
	[SerializeField] private MeshFilter _targetFilter;
	[SerializeField] private ParticleSystem _particleSystem;
	[SerializeField] private List<MeshFilter> _meshFilters;
	[SerializeField] private int _outlineLayerIndex = 1;
	[SerializeField] private float _scaleSpeed = 0.25f;
	[SerializeField] private float _effectOnCollisionTime = 1.0f;

	private float _currentTime = 0;
	
	private void OnValidate()
	{
		_interactable = GetComponent<Interactable>();
	}

	private void Start()
	{
		CopyMeshes();
		
		_interactable.IsBeingHeld.OnValueChanged += OnIsBeingHeldChanged;
	}

	private void OnDestroy()
	{
		_interactable.IsBeingHeld.OnValueChanged -= OnIsBeingHeldChanged;
	}

	private void Update()
	{
		if (_currentTime <= 0)
			return;

		_currentTime -= Time.deltaTime;
		if(_currentTime <= 0)
			_interactable.SetIsBeingHeldRpc(false);
	}

	public void ShowEffectForTime(float time)
	{
		_currentTime = time;
		_interactable.SetIsBeingHeldRpc(true);
	}

	private void OnCollisionEnter(Collision other)
	{
		ShowEffectForTime(_effectOnCollisionTime);
	}

	private void OnIsBeingHeldChanged(bool previousvalue, bool isHeld)
	{
		if (isHeld)
		{
			_meshRenderer.renderingLayerMask |= 1u << _outlineLayerIndex;
			_particleSystem.Play();
			_targetFilter.transform.DOScale(Vector3.one, _scaleSpeed);
		}
		else
		{
			_meshRenderer.renderingLayerMask &= ~(1u << _outlineLayerIndex);
			_particleSystem.Stop();
			_targetFilter.transform.DOScale(Vector3.zero, _scaleSpeed);
		}
	}

	private void CopyMeshes()
	{
		Mesh mesh = Instantiate(_meshFilters[0].sharedMesh);
		List<Vector3> verts = mesh.vertices.ToList();
		List<Vector2> uvs = mesh.uv.ToList();
		List<int> triangles = mesh.triangles.ToList();

		Dictionary<Vector3, int> uniqueVertIndexPointer = new Dictionary<Vector3, int>();
		List<Vector3> uniqueVerts = new List<Vector3>();
		int vertCount = verts.Count;
		int removedVerts = 0;
		for (int i = 0; i < vertCount - removedVerts; i++)
		{
			int vertIndex = 0;
			bool alreadyContains = false;
			for (int j = 0; j < uniqueVerts.Count; j++)
			{
				if (uniqueVerts[j] == verts[i])
				{
					vertIndex = j;
					alreadyContains = true;
					break;
				}
			}
			
			if (!alreadyContains)
			{
				uniqueVerts.Add(verts[i]);
				uniqueVertIndexPointer.TryAdd(verts[i], i);
				continue;
			}

			verts.RemoveAt(i);
			uvs.RemoveAt(i);
			removedVerts++;
			i--;
		}

		for (int i = 0; i < triangles.Count; i++)
		{
			if (uniqueVertIndexPointer.TryGetValue(_meshFilters[0].sharedMesh.vertices[triangles[i]], out int newIndex))
				triangles[i] = newIndex;
		}

		Mesh newMesh = new Mesh
		{
			vertices = verts.ToArray(),
			uv = uvs.ToArray(),
			triangles = triangles.ToArray()
		};
		newMesh.RecalculateNormals();
		_targetFilter.sharedMesh = newMesh;
		_targetFilter.transform.localScale = Vector3.zero;
	}
}
