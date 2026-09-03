using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An asset that defines the set of physical materials a <see cref="SpaceshipPart"/>
/// can be made from. Each material has a name and a density (mass per cubic unit),
/// which is combined with a part's mesh volume to derive its mass.
/// </summary>
[CreateAssetMenu(fileName = "PartMaterialLibrary", menuName = "Spaceship/Part Material Library")]
public class PartMaterialLibrary : ScriptableObject
{
	[System.Serializable]
	public class MaterialDefinition
	{
		[Tooltip("Display name used to select this material.")]
		public string Name = "New Material";

		[Tooltip("Mass per cubic world unit of volume. Part mass = mesh volume * density.")]
		[Min(0f)]
		public float Density = 1f;
	}

	[Tooltip("All materials available to spaceship parts.")]
	public List<MaterialDefinition> Materials = new();

	/// <summary>Returns the material with the given name, or null if not found.</summary>
	public MaterialDefinition GetByName(string materialName)
	{
		if (string.IsNullOrEmpty(materialName))
			return null;

		foreach (MaterialDefinition definition in Materials)
		{
			if (definition != null && definition.Name == materialName)
				return definition;
		}

		return null;
	}

	/// <summary>Returns the density for the named material, or 0 if not found.</summary>
	public float GetDensity(string materialName)
	{
		MaterialDefinition definition = GetByName(materialName);
		return definition != null ? definition.Density : 0f;
	}
}
