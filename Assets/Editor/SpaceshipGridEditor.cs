using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpaceshipGrid))]
[CanEditMultipleObjects]
public class SpaceshipGridEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		EditorGUILayout.Space();

		if (targets.Length == 1)
		{
			SpaceshipGrid grid = (SpaceshipGrid)target;
			Rigidbody rb = grid.GetComponent<Rigidbody>();
			if (rb != null)
			{
				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.FloatField("Grid Mass (Rigidbody)", rb.mass);
				}
			}
		}

		if (GUILayout.Button("Calculate Mass", GUILayout.Height(30)))
		{
			foreach (var t in targets)
			{
				if (t is SpaceshipGrid grid)
				{
					grid.CalculateMass();
				}
			}
		}
	}
}
