using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpaceshipPart))]
public class ShipPartEditor : Editor
{
	private bool isLinkingMode = false;

	public override void OnInspectorGUI()
	{
		SpaceshipPart currentPart = (SpaceshipPart)target;

		// Draw the default inspector but override how the mass fields are shown.
		DrawPropertiesExcluding(serializedObject, "m_Script", "materialName", "PartMass");

		SerializedProperty libraryProp = serializedObject.FindProperty("materialLibrary");
		SerializedProperty materialNameProp = serializedObject.FindProperty("materialName");

		PartMaterialLibrary library = libraryProp.objectReferenceValue as PartMaterialLibrary;

		bool materialChanged = false;

		if (library != null && library.Materials != null && library.Materials.Count > 0)
		{
			string[] names = new string[library.Materials.Count];
			for (int i = 0; i < names.Length; i++)
				names[i] = library.Materials[i] != null ? library.Materials[i].Name : "(null)";

			int currentIndex = Mathf.Max(0, System.Array.IndexOf(names, materialNameProp.stringValue));

			EditorGUI.BeginChangeCheck();
			int newIndex = EditorGUILayout.Popup("Material", currentIndex, names);
			if (EditorGUI.EndChangeCheck())
			{
				materialNameProp.stringValue = names[newIndex];
				materialChanged = true;
			}
		}
		else
		{
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(materialNameProp);
			if (EditorGUI.EndChangeCheck())
				materialChanged = true;

			if (library == null)
				EditorGUILayout.HelpBox("Assign a Part Material Library to pick a material by name.", MessageType.Info);
		}

		serializedObject.ApplyModifiedProperties();

		// When the material changes, recalculate the mass automatically.
		if (materialChanged)
		{
			Undo.RecordObject(currentPart, "Change Part Material");
			currentPart.RecalculateMass();
			EditorUtility.SetDirty(currentPart);
		}

		// Show mass read-only; it is only updated via the button below.
		using (new EditorGUI.DisabledScope(true))
		{
			EditorGUILayout.FloatField("Part Mass", currentPart.PartMass);
			EditorGUILayout.FloatField("Mesh Volume", currentPart.CalculateVolume());
		}

		if (GUILayout.Button("Calculate Mass"))
		{
			Undo.RecordObject(currentPart, "Calculate Part Mass");
			currentPart.RecalculateMass();
			EditorUtility.SetDirty(currentPart);
		}

		EditorGUILayout.Space();

		GUI.backgroundColor = isLinkingMode ? Color.green : Color.white;

		if (GUILayout.Button(isLinkingMode ? "Linking Mode Active (Click another part)" : "Enter Linking Mode", GUILayout.Height(30)))
		{
			isLinkingMode = !isLinkingMode;
			ActiveEditorTracker.sharedTracker.isLocked = isLinkingMode;
		}
		GUI.backgroundColor = Color.white;

		if (GUILayout.Button("Clear Welds"))
		{
			Undo.RecordObject(currentPart, "Clear Welds");

			foreach (SpaceshipPart part in currentPart.connectedParts)
			{
				part.connectedParts.Remove(currentPart);
			}
			
			currentPart.connectedParts.Clear();
			EditorUtility.SetDirty(currentPart);
		}
	}

	private void OnSceneGUI()
	{
		if (!isLinkingMode) return;

		SpaceshipPart currentPart = (SpaceshipPart)target;
		Event e = Event.current;

		DrawTargetingLine(currentPart, e);

		if (e.type == EventType.MouseDown && e.button == 0)
		{
			GameObject clickedObj = HandleUtility.PickGameObject(e.mousePosition, false);

			if (clickedObj != null)
			{
				SpaceshipPart clickedPart = clickedObj.GetComponent<SpaceshipPart>();

				if (clickedPart != null && clickedPart != currentPart)
				{
					ConnectParts(currentPart, clickedPart);
				}
			}

			e.Use();
		}

		HandleUtility.Repaint();
	}

	private void ConnectParts(SpaceshipPart partA, SpaceshipPart partB)
	{
		// Record undo steps for both objects
		Undo.RecordObject(partA, "Weld Ship Parts");
		Undo.RecordObject(partB, "Weld Ship Parts");

		// Make the connection bidirectional
		if (!partA.connectedParts.Contains(partB)) partA.connectedParts.Add(partB);
		if (!partB.connectedParts.Contains(partA)) partB.connectedParts.Add(partA);

		EditorUtility.SetDirty(partA);
		EditorUtility.SetDirty(partB);

		Debug.Log($"Welded {partA.name} to {partB.name}");
	}

	private void DrawTargetingLine(SpaceshipPart currentPart, Event e)
	{
		Handles.color = Color.green;
		Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
		Vector3 endPoint = ray.origin + ray.direction * 10f;

		Plane plane = new Plane(Camera.current.transform.forward, currentPart.transform.position);
		if (plane.Raycast(ray, out float enter))
		{
			endPoint = ray.GetPoint(enter);
		}

		Handles.DrawDottedLine(currentPart.transform.position, endPoint, 4f);
	}

	private void OnDisable()
	{
		ActiveEditorTracker.sharedTracker.isLocked = false;
		isLinkingMode = false;
	}
}