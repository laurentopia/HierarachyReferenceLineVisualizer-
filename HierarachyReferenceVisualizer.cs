using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.Experimental.SceneManagement;

[InitializeOnLoad]
public class HierarachyReferenceVisualizer
{
	static GameObject           selectedObject;
	static HashSet<GameObject>  referencedObjects  = new HashSet<GameObject>();
	static HashSet<GameObject>  referencingObjects = new HashSet<GameObject>();
	static Dictionary<int,Rect> itemRects          = new Dictionary<int,Rect>();
	static Rect                 selectedRect;

	static HierarachyReferenceVisualizer()
	{
		Selection.selectionChanged                 += OnSelectionChanged;
		EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyWindowItemOnGUI;
	}

	static void OnSelectionChanged()
	{
		if(PrefabStageUtility.GetCurrentPrefabStage()==null) return;
		selectedObject = Selection.activeGameObject;
		UpdateReferences();
	}

	static void UpdateReferences()
	{
		referencedObjects.Clear();
		referencingObjects.Clear();
		if(selectedObject==null) return;

		// Find referenced objects **********
		var components = selectedObject.GetComponents<Component>();
		foreach (var component in components) {
			if(component==null || component.GetType()==typeof(Transform)) continue; // ignore transforms so we don't get lines to root and parent
			var fields = component.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
			foreach (var field in fields) {
				if(typeof(Object).IsAssignableFrom(field.FieldType)) {
					var value = field.GetValue(component) as Object;
					AddReferencedObject(value);
				} else if(typeof(IEnumerable<Object>).IsAssignableFrom(field.FieldType)) {
					var enumerable = field.GetValue(component) as IEnumerable<Object>;
					if(enumerable!=null) {
						foreach (var item in enumerable) AddReferencedObject(item);
					}
				} else if(field.FieldType.IsArray && typeof(Object).IsAssignableFrom(field.FieldType.GetElementType())) {
					var array = field.GetValue(component) as Object[];
					if(array!=null) {
						foreach (var item in array) AddReferencedObject(item);
					}
				}
			}

			// Also check properties
			var properties = component.GetType().GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
			foreach (var property in properties) {
				if(property.CanRead && typeof(Object).IsAssignableFrom(property.PropertyType)) {
					try {
						if(component is Renderer || component is MeshFilter) continue;
						var value = property.GetValue(component,null) as Object;
						AddReferencedObject(value);
					} catch { } // Skip properties that can't be accessed
				}
			}
		}

		// Find referencing objects *********
		GameObject[] allGameObjects;
		if(PrefabStageUtility.GetCurrentPrefabStage()!=null) {
			// In prefab edit mode
			allGameObjects = PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot.GetComponentsInChildren<Transform>().Select(t => t.gameObject).ToArray();
		} else {
			// In normal scene mode
			allGameObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
		}
		foreach (var go in allGameObjects) {
			if(go==selectedObject) continue;
			bool isReferencing = false;
			var  comps         = go.GetComponents<Component>();
			foreach (var comp in comps) {
				if(comp==null || comp.GetType()==typeof(Transform)) continue; // ignore transforms so we don't get lines to root and parent
				// Check fields
				var fields = comp.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
				foreach (var field in fields) {
					if(typeof(Object).IsAssignableFrom(field.FieldType)) {
						var value = field.GetValue(comp) as Object;
						if(IsReferencingSelectedObject(value)) {
							isReferencing = true;
							break;
						}
					} else if(typeof(IEnumerable<Object>).IsAssignableFrom(field.FieldType)) {
						var enumerable = field.GetValue(comp) as IEnumerable<Object>;
						if(enumerable!=null) {
							foreach (var item in enumerable) {
								if(IsReferencingSelectedObject(item)) {
									isReferencing = true;
									break;
								}
							}
						}
					}
				}

				// Check properties
				var properties = comp.GetType().GetProperties(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
				foreach (var property in properties) {
					if(property.CanRead && typeof(Object).IsAssignableFrom(property.PropertyType)) {
						try {
							if(comp is Renderer || comp is MeshFilter) continue;
							var value = property.GetValue(comp,null) as Object;
							if(IsReferencingSelectedObject(value)) {
								isReferencing = true;
								break;
							}
						} catch { } // Skip properties that can't be accessed
					}
				}
				if(isReferencing) break;
			}
			if(isReferencing) { referencingObjects.Add(go); }
		}
	}

	static bool IsReferencingSelectedObject(Object obj)
	{
		if(!obj) return false;
		if(obj==selectedObject) return true;
		if(obj is Component comp && comp.gameObject==selectedObject) return true;
		return false;
	}

	static void AddReferencedObject(Object obj)
	{
		if(obj==null) return;
		GameObject go = null;
		if(obj is GameObject gameObject) { go = gameObject; } else if(obj is Component comp) { go = comp.gameObject; }
		if(go!=null && go!=selectedObject) { referencedObjects.Add(go); }
	}

	static void OnHierarchyWindowItemOnGUI(int instanceID,Rect rect)
	{
		if(PrefabStageUtility.GetCurrentPrefabStage()==null) return;
		if(selectedObject==null) return;
		var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
		if(obj==null) return;
		// if(obj!=selectedObject) return;
		itemRects[instanceID] = rect;
		if(obj==selectedObject) selectedRect = rect;
		if(Event.current.type==EventType.Repaint) DrawCurves();
	}

	static void DrawCurves()
	{
		if(selectedRect==Rect.zero) return;
		Handles.BeginGUI();
		foreach (var go in referencedObjects) { DrawLineToObject(go,Color.blue,false); }
		foreach (var go in referencingObjects) { DrawLineToObject(go,Color.red,true); }
		Handles.EndGUI();
	}

	static void DrawLineToObject(GameObject go,Color color,bool isReferencing)
	{
		int id = go.GetInstanceID();
		if(itemRects.TryGetValue(id,out Rect targetRect)) {
			if(isReferencing) DrawBezier(targetRect,selectedRect,color);
			else DrawBezier(selectedRect,targetRect,color);
		} else {
			// Handle objects outside the visible area
			Rect edgeRect = GetEdgeRect(isReferencing);
			if(isReferencing) DrawBezier(edgeRect,selectedRect,color);
			else DrawBezier(selectedRect,edgeRect,color);
		}
	}

	static Rect GetEdgeRect(bool isReferencing)
	{
		Rect hierarchyRect = GetHierarchyWindowRect();
		Rect edgeRect      = selectedRect;
		if(isReferencing) edgeRect.y = hierarchyRect.yMin;                     // Top edge
		else edgeRect.y              = hierarchyRect.yMax-selectedRect.height; // Bottom edge
		return edgeRect;
	}

	static void DrawBezier(Rect fromRect,Rect toRect,Color color)
	{
		Vector2 startPos     = new Vector2(fromRect.xMin+15,fromRect.y+fromRect.height/2);
		Vector2 endPos       = new Vector2(toRect.xMin,toRect.y+toRect.height/2);
		Vector2 startTangent = startPos+Vector2.right*50;
		Vector2 endTangent   = endPos+Vector2.left*50;
		Handles.color = color;
		Handles.DrawBezier(startPos,endPos,startTangent,endTangent,color,null,1);
	}

	static Rect GetHierarchyWindowRect()
	{
		foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()) {
			if(window.titleContent.text=="Hierarchy") return window.position;
		}
		return new Rect(0,0,Screen.width,Screen.height);
	}
}