#if UNITY_5_5_OR_NEWER
#define RETINA_ENABLED
#endif

using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor.ProBuilder;
using System.Reflection;
using UnityEngine.ProBuilder;
using UnityEditor.ProBuilder.UI;
using UnityEngine.ProBuilder.MeshOperations;
using UnityEditor.SettingsManagement;

namespace UnityEditor.ProBuilder
{
	sealed class UVEditor : ConfigurableWindow
	{
#region Fields

		ProBuilderEditor editor
		{
			get { return ProBuilderEditor.instance; }
		}

		public static UVEditor instance;

		const int LEFT_MOUSE_BUTTON = 0;
		const int RIGHT_MOUSE_BUTTON = 1;
		const int MIDDLE_MOUSE_BUTTON = 2;
		const int PAD = 4;
		const float SCROLL_MODIFIER = 1f;
		const float ALT_SCROLL_MODIFIER = .07f;
		const int DOT_SIZE = 6;
		const int HALF_DOT = 3;
		const int HANDLE_SIZE = 128;
		const int MIN_ACTION_WINDOW_SIZE = 128;
		const float MIN_GRAPH_SCALE = .0001f;
		const float MAX_GRAPH_SCALE = 250f;
		// Max canvas zoom
		const float MAX_GRAPH_SCALE_SCROLL = 20f;
		// When scrolling use this value to taper the scroll effect
		const float MAX_PROXIMITY_SNAP_DIST_UV = .15f;
		// The maximum allowable distance magnitude between coords to be considered for proximity snapping (UV coordinates)
		const float MAX_PROXIMITY_SNAP_DIST_CANVAS = 12f;
		// The maximum allowable distance magnitude between coords to be considered for proximity snapping (Canvas coordinates)
		const float MIN_DIST_MOUSE_EDGE = 8f;

		// todo Support Range/Min/Max property decorators
		[UserSetting]
		static Pref<float> s_GridSnapIncrement = new Pref<float>("uv.uvEditorGridSnapIncrement", .125f, SettingsScope.Project);

		[UserSettingBlock("UV Editor")]
		static void UVEditorSettings(string searchContext)
		{
			s_GridSnapIncrement.value = SettingsGUILayout.SettingsSlider(UI.EditorGUIUtility.TempContent("Grid Size"), s_GridSnapIncrement, .015625f, 2f, searchContext);
		}

		static readonly Color DRAG_BOX_COLOR_BASIC = new Color(0f, .7f, 1f, .2f);
		static readonly Color DRAG_BOX_COLOR_PRO = new Color(0f, .7f, 1f, 1f);

		static Color DRAG_BOX_COLOR
		{
			get { return EditorGUIUtility.isProSkin ? DRAG_BOX_COLOR_PRO : DRAG_BOX_COLOR_BASIC; }
		}

		static readonly Color HOVER_COLOR_MANUAL = new Color(1f, .68f, 0f, .23f);
		static readonly Color HOVER_COLOR_AUTO = new Color(0f, 1f, 1f, .23f);

		static readonly Color SELECTED_COLOR_MANUAL = new Color(1f, .68f, 0f, .39f);
		static readonly Color SELECTED_COLOR_AUTO = new Color(0f, .785f, 1f, .39f);

#if UNITY_STANDALONE_OSX
	public bool ControlKey { get { return Event.current.modifiers == EventModifiers.Command; } }
	#else
		public bool ControlKey
		{
			get { return Event.current.modifiers == EventModifiers.Control; }
		}
#endif
		public bool ShiftKey
		{
			get { return Event.current.modifiers == EventModifiers.Shift; }
		}

		Pref<bool> m_ShowPreviewMaterial = new Pref<bool>("UVEditor.showPreviewMaterial", true, SettingsScope.Project);

		// Show a preview texture for the first selected face in UV space 0,1?
#if PB_DEBUG
	List<Texture2D> m_DebugUVRenderScreens = new List<Texture2D>();
	#endif
		Color GridColorPrimary;
		Color BasicBackgroundColor;
		Color UVColorPrimary, UVColorSecondary, UVColorGroupIndicator;

		Texture2D dot,
			icon_textureMode_on,
			icon_textureMode_off,
			icon_sceneUV_on,
			icon_sceneUV_off;

		GUIContent gc_SceneViewUVHandles = new GUIContent("", (Texture2D)null, "Lock the SceneView handle tools to UV manipulation mode.  This allows you to move UV coordinates directly on your 3d object.");
		GUIContent gc_ShowPreviewTexture = new GUIContent("", (Texture2D)null, "When toggled on, a preview image of the first selected face's material will be drawn from coordinates 0,0 - 1,1.\n\nNote that this depends on the Material's shader having a _mainTexture property.");

		GUIContent gc_ConvertToManual = new GUIContent("Convert to Manual", "There are 2 methods of unwrapping UVs in ProBuilder; Automatic unwrapping and Manual.  Auto unwrapped UVs are generated dynamically using a set of parameters, which may be set.  Manual UVs are akin to traditional UV unwrapping, in that once you set them they will not be updated as your mesh changes.");
		GUIContent gc_ConvertToAuto = new GUIContent("Convert to Auto", "There are 2 methods of unwrapping UVs in ProBuilder; Automatic unwrapping and Manual.  Auto unwrapped UVs are generated dynamically using a set of parameters, which may be set.  Manual UVs are akin to traditional UV unwrapping, in that once you set them they will not be updated as your mesh changes.");

		GUIContent gc_RenderUV = new GUIContent((Texture2D)null, "Renders the current UV workspace from coordinates {0,0} to {1,1} to a 256px image.");

		// Full grid size in pixels (-1, 1)
		private int uvGridSize = 256;
		private float uvGraphScale = 1f;

		enum UVMode
		{
			Auto,
			Manual,
			Mixed
		};

		UVMode mode = UVMode.Auto;

		int[] UV_CHANNELS = new int[] { 0, 1, 2, 3 };
		string[] UV_CHANNELS_STR = new string[] { "UV 1", "UV 2 (read-only)", "UV 3 (read-only)", "UV 4 (read-only)" };

#if PB_DEBUG
	bool debug_showCoordinates = false;
	#endif

		// what uv channel to modify
		int channel = 0;

		private Vector2 uvGraphOffset = Vector2.zero;

		/// inspected data
		ProBuilderMesh[] selection;
		int[][] m_DistinctIndexesSelection;

		List<Face[]>[] incompleteTextureGroupsInSelection = new List<Face[]>[0];
		List<List<Vector2>> incompleteTextureGroupsInSelection_CoordCache = new List<List<Vector2>>();

		int selectedUVCount = 0;
		int selectedFaceCount = 0;
		int screenWidth, screenHeight;

		// true when uvs are being moved around
		bool modifyingUVs = false;

		// work around a bug in GUI where a named control can lose focus after "delete"
		bool eatNextKeyUp = false;

		// The first selected face's material.  Used to draw texture preview in 0,0 - 1,1 space.
		Material m_PreviewMaterial;

		Tool tool = Tool.Move;

		GUIContent[] ToolIcons;
		GUIContent[] SelectionIcons;

		struct ObjectElementIndex
		{
			public int objectIndex;
			public int elementIndex;
			public int elementSubIndex;
			public bool valid;

			public void Clear()
			{
				this.objectIndex = -1;
				this.elementIndex = -1;
				this.elementSubIndex = -1;
				this.valid = false;
			}

			public ObjectElementIndex(int obj, int elem, int sub)
			{
				this.objectIndex = obj;
				this.elementIndex = elem;
				this.elementSubIndex = sub;
				this.valid = false;
			}

			public bool Equals(ObjectElementIndex oei)
			{
				return this.objectIndex == oei.objectIndex &&
					this.elementIndex == oei.elementIndex &&
					this.elementSubIndex == oei.elementSubIndex &&
					this.valid == oei.valid;
			}

			public override string ToString()
			{
				return valid ? objectIndex + " : " + elementIndex + " -> " + elementSubIndex : "Invalid";
			}
		}

		ObjectElementIndex nearestElement = new ObjectElementIndex(-1, -1, -1);
#endregion
#region Menu

		public static void MenuOpenUVEditor()
		{
			GetWindow<UVEditor>("UV Editor");
		}

		void ScreenshotMenu()
		{
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX

// On Mac ShowAsDropdown and ShowAuxWindow both throw stack pop exceptions when initialized.
		UVRenderOptions renderOptions = EditorWindow.GetWindow<UVRenderOptions>(true, "Save UV Image", true);
		renderOptions.position = new Rect(	this.position.x + (this.position.width/2f - 128),
											this.position.y + (this.position.height/2f - 76),
											256f,
											152f);
		renderOptions.screenFunc = InitiateScreenshot;
#else
			UVRenderOptions renderOptions = (UVRenderOptions)ScriptableObject.CreateInstance(typeof(UVRenderOptions));
			renderOptions.screenFunc = InitiateScreenshot;
			renderOptions.ShowAsDropDown(new Rect(this.position.x + 348,
					this.position.y + 32,
					0,
					0),
				new Vector2(256, 152));
#endif
		}
#endregion
#region Enable

		void OnEnable()
		{
			this.minSize = new Vector2(500f, 300f);

			InitGUI();

			this.wantsMouseMove = true;
			this.autoRepaintOnSceneChange = true;

			ProBuilderEditor.selectionUpdated += OnSelectionUpdate;
			if (editor != null)
				OnSelectionUpdate(editor.selection);

			instance = this;

			ProBuilderMeshEditor.onGetFrameBoundsEvent += OnGetFrameBoundsEvent;

			nearestElement.Clear();
		}

		void OnDisable()
		{
			instance = null;

			if(ProBuilderEditor.selectMode == SelectMode.TextureFace)
				ProBuilderEditor.ResetToLastSelectMode();

			if (uv2Editor != null)
				Object.DestroyImmediate(uv2Editor);

			// EditorApplication.delayCall -= this.Close;							// not sure if this is necessary?
			ProBuilderEditor.selectionUpdated -= OnSelectionUpdate;
			ProBuilderMeshEditor.onGetFrameBoundsEvent -= OnGetFrameBoundsEvent;
		}

		/**
		 * Loads icons, sets default colors using prefs, etc.
		 */
		void InitGUI()
		{
			bool isProSkin = true;

			GridColorPrimary = isProSkin ? new Color(1f, 1f, 1f, .2f) : new Color(0f, 0f, 0f, .2f);
			UVColorPrimary = isProSkin ? Color.green : new Color(0f, .8f, 0f, 1f);
			UVColorSecondary = isProSkin ? new Color(1f, 1f, 1f, .7f) : Color.blue;
			UVColorGroupIndicator = isProSkin ? new Color(0f, 1f, .2f, .15f) : new Color(0f, 1f, .2f, .3f);
			BasicBackgroundColor = new Color(.24f, .24f, .24f, 1f);

			dot = EditorGUIUtility.whiteTexture;

			MethodInfo loadIconMethod = typeof(EditorGUIUtility).GetMethod("LoadIcon", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

			isProSkin = EditorGUIUtility.isProSkin;

			Texture2D moveIcon = (Texture2D)loadIconMethod.Invoke(null, new object[] { "MoveTool" });
			Texture2D rotateIcon = (Texture2D)loadIconMethod.Invoke(null, new object[] { "RotateTool" });
			Texture2D scaleIcon = (Texture2D)loadIconMethod.Invoke(null, new object[] { "ScaleTool" });
			Texture2D viewIcon = (Texture2D)loadIconMethod.Invoke(null, new object[] { "ViewToolMove" });

			Texture2D face_Graphic_off = IconUtility.GetIcon("Modes/Mode_Face");
			Texture2D vertex_Graphic_off = IconUtility.GetIcon("Modes/Mode_Vertex");
			Texture2D edge_Graphic_off = IconUtility.GetIcon("Modes/Mode_Edge");

			icon_textureMode_on = IconUtility.GetIcon("UVEditor/ProBuilderGUI_UV_ShowTexture_On", IconSkin.Pro);
			icon_textureMode_off = IconUtility.GetIcon("UVEditor/ProBuilderGUI_UV_ShowTexture_Off", IconSkin.Pro);

			icon_sceneUV_on = IconUtility.GetIcon("UVEditor/ProBuilderGUI_UV_Manip_On", IconSkin.Pro);
			icon_sceneUV_off = IconUtility.GetIcon("UVEditor/ProBuilderGUI_UV_Manip_Off", IconSkin.Pro);

			gc_RenderUV.image = IconUtility.GetIcon("UVEditor/camera-64x64");

			ToolIcons = new GUIContent[4]
			{
				new GUIContent(viewIcon, "View Tool"),
				new GUIContent(moveIcon, "Move Tool"),
				new GUIContent(rotateIcon, "Rotate Tool"),
				new GUIContent(scaleIcon, "Scale Tool")
			};

			SelectionIcons = new GUIContent[3]
			{
				new GUIContent(vertex_Graphic_off, "Vertex Selection"),
				new GUIContent(edge_Graphic_off, "Edge Selection"),
				new GUIContent(face_Graphic_off, "Face Selection")
			};
		}
#endregion
#region GUI Loop

		const int k_UVInspectorWidthMinManual = 100;
		const int k_UVInspectorWidthMinAuto = 200;
		const int k_UVInspectorWidth = 210;

		Rect graphRect,
			toolbarRect,
			actionWindowRect = new Rect(6, 64, k_UVInspectorWidth, 340);

		Vector2 mousePosition_initial;

		Rect dragRect = new Rect(0, 0, 0, 0);
		bool m_mouseDragging = false;

		bool needsRepaint = false;
		Rect ScreenRect = new Rect(0f, 0f, 0f, 0f);

		enum ScreenshotStatus
		{
			PrepareCanvas,
			CanvasReady,
			RenderComplete,
			Done
		}

		ScreenshotStatus screenshotStatus = ScreenshotStatus.Done;

		void OnGUI()
		{
			if (screenshotStatus != ScreenshotStatus.Done)
			{
				minSize = new Vector2(ScreenRect.width, ScreenRect.height);
				maxSize = new Vector2(ScreenRect.width, ScreenRect.height);

				UI.EditorGUIUtility.DrawSolidColor(new Rect(-1, -1, ScreenRect.width + 10, ScreenRect.height + 10), screenshot_backgroundColor);

				DrawUVGraph(graphRect);

				if (screenshotStatus == ScreenshotStatus.PrepareCanvas)
				{
					if (Event.current.type == EventType.Repaint)
					{
						screenshotStatus = ScreenshotStatus.CanvasReady;
						DoScreenshot();
					}

					return;
				}
				else
				{
					DoScreenshot();
				}
			}

			if (tool == Tool.View || m_draggingCanvas)
				EditorGUIUtility.AddCursorRect(new Rect(0, toolbarRect.y + toolbarRect.height, screenWidth, screenHeight), MouseCursor.Pan);

			ScreenRect.width = this.position.width;
			ScreenRect.height = this.position.height;

			// if basic skin, manually tint the background
			if (!EditorGUIUtility.isProSkin)
			{
				GUI.backgroundColor = BasicBackgroundColor; //new Color(.13f, .13f, .13f, .7f);
				GUI.Box(ScreenRect, "");
				GUI.backgroundColor = Color.white;
			}

			if (!Math.Approx(position.width, screenWidth) || !Math.Approx(position.height, screenHeight))
				OnScreenResize();

			toolbarRect = new Rect(PAD, PAD, this.position.width - PAD * 2, 29);
			graphRect = new Rect(PAD, PAD, this.position.width - PAD * 2, this.position.height - PAD * 2);

			actionWindowRect.x = (int)Mathf.Clamp(actionWindowRect.x, PAD, position.width - PAD - PAD - actionWindowRect.width);
			actionWindowRect.y = (int)Mathf.Clamp(actionWindowRect.y, PAD, position.height - MIN_ACTION_WINDOW_SIZE);
			if (actionWindowRect.y + actionWindowRect.height > position.height)
				actionWindowRect.height = position.height - actionWindowRect.y - 24;
			int minWidth = (mode == UVMode.Auto ? k_UVInspectorWidthMinAuto : k_UVInspectorWidthMinManual);
			if (actionWindowRect.width < minWidth)
				actionWindowRect.width = minWidth;

			// Mouse drags, canvas movement, etc
			HandleInput();

			DrawUVGraph(graphRect);

			// Draw AND update translation handles
			if (channel == 0 && selection != null && selectedUVCount > 0)
			{
				switch (tool)
				{
					case Tool.Move:
						MoveTool();
						break;

					case Tool.Rotate:
						RotateTool();
						break;

					case Tool.Scale:
						ScaleTool();
						break;
				}
			}

			if (channel == 0 && UpdateNearestElement(Event.current.mousePosition))
				Repaint();

			if (m_mouseDragging && EditorHandleUtility.CurrentID < 0 && !m_draggingCanvas && !m_rightMouseDrag)
			{
				Color oldColor = GUI.backgroundColor;
				GUI.backgroundColor = DRAG_BOX_COLOR;
				GUI.Box(dragRect, "");
				GUI.backgroundColor = oldColor;
			}

			DrawUVTools(toolbarRect);

			// for now only uv channels 0 and 1 are editable in any way
			if (channel == 0 || channel == 1)
			{
				BeginWindows();
				actionWindowRect = GUILayout.Window(1, actionWindowRect, DrawActionWindow, "Actions");
				EndWindows();
			}

			if (needsRepaint)
			{
				Repaint();
				needsRepaint = false;
			}

#if PB_DEBUG
		buggerRect = new Rect(this.position.width - 226, PAD, 220, 300);
		DrawDebugInfo(buggerRect);
		#endif
		}
#endregion
#region Editor Delegate and Event

		void OnSelectionUpdate(ProBuilderMesh[] selection)
		{
			this.selection = selection;

			SetSelectedUVsWithSceneView();

			RefreshUVCoordinates();

			// get incompletely selected texture groups
			int len = selection == null ? 0 : selection.Length;

			incompleteTextureGroupsInSelection = new List<Face[]>[len];
			incompleteTextureGroupsInSelection_CoordCache.Clear();

			for (int i = 0; i < len; i++)
			{
				incompleteTextureGroupsInSelection[i] = GetIncompleteTextureGroups(selection[i], selection[i].selectedFacesInternal);

				if (incompleteTextureGroupsInSelection[i].Count < 1)
				{
					continue;
				}
				else
				{
					ProBuilderMesh pb = selection[i];

					foreach (Face[] incomplete_group in incompleteTextureGroupsInSelection[i])
					{
						if (incomplete_group == null || incomplete_group.Length < 1)
							continue;

						List<Vector2> coords = new List<Vector2>();

						foreach (Face face in incomplete_group)
							coords.Add(Bounds2D.Center(pb.texturesInternal.ValuesWithIndexes(face.distinctIndexesInternal)));

						coords.Insert(0, Bounds2D.Center(coords.ToArray()));

						incompleteTextureGroupsInSelection_CoordCache.Add(coords);
					}
				}
			}

			Repaint();
		}

		/**
		 * Automatically select textureGroup buddies, and copy origins of all UVs.
		 * Also resets the mesh to PB data, removing vertices appended by
		 * UV2 generation.
		 */
		internal void OnBeginUVModification()
		{
			Lightmapping.PushGIWorkflowMode();

			modifyingUVs = true;

			bool update = false;

			// Make sure all TextureGroups are auto-selected
			for (int i = 0; i < selection.Length; i++)
			{
				if (selection[i].selectedFaceCount > 0)
				{
					int fc = selection[i].selectedFaceCount;
					selection[i].SetSelectedFaces(SelectTextureGroups(selection[i], selection[i].selectedFacesInternal));

					// kinda lame... this will cause setSelectedUVsWithSceneView to be called again.
					if (fc != selection[i].selectedFaceCount)
						update = true;
				}

				selection[i].ToMesh(); // Reset the Mesh to PB data only.
				selection[i].Refresh();
			}

			if (update)
			{
				/// UpdateSelection clears handlePosition
				Vector2 storedHandlePosition = handlePosition;
				ProBuilderEditor.Refresh();
				SetHandlePosition(storedHandlePosition, true);
			}

			CopySelectionUVs(out uv_origins);
			uvOrigin = handlePosition;
		}

		/**
		 * Internal because pb_Editor needs to call this sometimes.
		 */
		internal void OnFinishUVModification()
		{
			Lightmapping.PopGIWorkflowMode();

			modifyingUVs = false;

			if ((tool == Tool.Rotate || tool == Tool.Scale) && userPivot)
				SetHandlePosition(handlePosition, true);

			if (mode == UVMode.Mixed || mode == UVMode.Auto)
			{
				UndoUtility.RegisterCompleteObjectUndo(selection, (tool == Tool.Move ? "Translate UVs" : tool == Tool.Rotate ? "Rotate UVs" : "Scale UVs"));

				foreach (ProBuilderMesh pb in selection)
				{
					if (pb.selectedFaceCount > 0)
					{
						// Sort faces into texture groups for re-projection
						Dictionary<int, List<Face>> textureGroups = new Dictionary<int, List<Face>>();

						int n = -2;
						foreach (Face face in System.Array.FindAll(pb.selectedFacesInternal, x => !x.manualUV))
						{
							if (textureGroups.ContainsKey(face.textureGroup))
								textureGroups[face.textureGroup].Add(face);
							else
								textureGroups.Add(face.textureGroup > 0 ? face.textureGroup : n--, new List<Face>() { face });
						}

						foreach (KeyValuePair<int, List<Face>> kvp in textureGroups)
						{
							if (tool == Tool.Move)
							{
								foreach (Face face in kvp.Value)
								{
									var uv = face.uv;
									uv.offset -= handlePosition - handlePosition_origin;
									face.uv = uv;
								}
							}
							else if (tool == Tool.Rotate)
							{
								foreach (Face face in kvp.Value)
								{
									var uv = face.uv;

									if (uv.rotation > 360f)
										uv.rotation = uv.rotation % 360f;
									else if (uv.rotation < 0f)
										uv.rotation = 360f + (uv.rotation % 360f);

									face.uv = uv;
								}
							}
						}
					}
					else
					{
						FlagSelectedFacesAsManual(pb);
					}
				}
			}
			else if (mode == UVMode.Manual)
			{
				foreach (ProBuilderMesh pb in selection)
				{
					if (pb.selectedFaceCount > 0)
					{
						foreach (Face face in pb.selectedFacesInternal)
						{
							face.textureGroup = -1;
							face.manualUV = true;
						}
					}
					else
					{
						FlagSelectedFacesAsManual(pb);
					}
				}
			}

			// Regenerate UV2s
			foreach (ProBuilderMesh pb in selection)
			{
				pb.ToMesh();
				pb.Refresh();
				pb.Optimize();
			}
		}

		void SetSelectedUVsWithSceneView()
		{
			if (selection == null)
			{
				m_DistinctIndexesSelection = new int[0][];
				return;
			}

			m_DistinctIndexesSelection = new int[selection.Length][];

			// Append shared UV indexes to SelectedTriangles array (if necessary)
			for (int i = 0; i < selection.Length; i++)
			{
				List<int> selectedTris = new List<int>(selection[i].selectedIndexesInternal);

				SharedVertex[] sharedUVs = selection[i].sharedTextures;

				// put sewn UVs into the selection if they aren't already
				if (sharedUVs != null)
				{
					foreach (var arr in sharedUVs)
					{
						if (System.Array.Exists(arr.arrayInternal, element => System.Array.IndexOf(selection[i].selectedIndexesInternal, element) > -1))
						{
							selectedTris.AddRange(arr);
						}
					}
				}

				m_DistinctIndexesSelection[i] = selectedTris.Distinct().ToArray();
			}
		}

		void OnGetFrameBoundsEvent()
		{
			FrameSelection();
			Repaint();
		}

		void OnScreenResize()
		{
			screenWidth = (int)this.position.width;
			screenHeight = (int)this.position.height;
			RefreshUVCoordinates();
			Repaint();
		}

		/**
		 * return true if shortcut should eat the event
		 */
		internal bool ClickShortcutCheck(ProBuilderMesh pb, Face selectedFace)
		{
			Event e = Event.current;

			// Copy UV settings
			if (e.modifiers == (EventModifiers.Control | EventModifiers.Shift))
			{
				// get first selected Auto UV face
				ProBuilderMesh firstObj;
				Face source;

				ProBuilderEditor.instance.GetFirstSelectedFace(out firstObj, out source);

				if (source != null)
				{
					UndoUtility.RecordObject(pb, "Copy UV Settings");

					selectedFace.uv = new AutoUnwrapSettings(source.uv);
					selectedFace.submeshIndex = source.submeshIndex;
					EditorUtility.ShowNotification("Copy UV Settings");

					pb.ToMesh();
					pb.Refresh();
					pb.Optimize();

					RefreshUVCoordinates();

					Repaint();

					return true;
				}
				else
				{
					return false;
				}
			}
			else if (e.modifiers == EventModifiers.Control)
			{
				int len = pb.selectedFacesInternal == null ? 0 : pb.selectedFacesInternal.Length;

				if (len < 1)
					return false;

				Face anchor = pb.selectedFacesInternal[len - 1];

				if (anchor == selectedFace)
					return false;

				UndoUtility.RecordObject(pb, "AutoStitch");

				pb.ToMesh();

				bool success = UVEditing.AutoStitch(pb, anchor, selectedFace, channel);

				if (success)
				{
					RefreshElementGroups(pb);

					pb.SetSelectedFaces(new Face[] { selectedFace });

					// // only need to do this for one pb_Object...
					// for(int i = 0; i < selection.Length; i++)
					// 	selection[i].RefreshUV( editor.SelectedFacesInEditZone[i] );

					pb.Refresh();
					pb.Optimize();

					SetSelectedUVsWithSceneView();

					RefreshUVCoordinates();

					EditorUtility.ShowNotification("Autostitch");

					ProBuilderEditor.Refresh();

					Repaint();
				}
				else
				{
					pb.Refresh();
					pb.Optimize();
				}

				return success;
			}

			return false;
		}
#endregion
#region Key and Handle Input

		bool m_ignore = false;
		bool m_rightMouseDrag = false;
		bool m_draggingCanvas = false;
		bool m_doubleClick = false;

		void HandleInput()
		{
			Event e = Event.current;

			if (e.isKey)
			{
				HandleKeyInput(e);
				return;
			}

			switch (e.type)
			{
				case EventType.MouseDown:

#if PB_DEBUG
				if(toolbarRect.Contains(e.mousePosition) || actionWindowRect.Contains(e.mousePosition) || buggerRect.Contains(e.mousePosition))
				#else
					if (toolbarRect.Contains(e.mousePosition) || actionWindowRect.Contains(e.mousePosition))
#endif
					{
						m_ignore = true;
						return;
					}

					if (e.clickCount > 1)
						m_doubleClick = true;

					mousePosition_initial = e.mousePosition;

					break;

				case EventType.MouseDrag:

					if (m_ignore || (e.mousePosition.y <= toolbarRect.y && !m_mouseDragging))
						break;

					m_mouseDragging = true;

					if (e.button == RIGHT_MOUSE_BUTTON || (e.button == LEFT_MOUSE_BUTTON && e.alt))
						m_rightMouseDrag = true;

					needsRepaint = true;

					/* If no handle is selected, do other stuff */
					if (EditorHandleUtility.CurrentID < 0)
					{
						if ((e.alt && e.button == LEFT_MOUSE_BUTTON) || e.button == MIDDLE_MOUSE_BUTTON || Tools.current == Tool.View)
						{
							m_draggingCanvas = true;
							uvGraphOffset += e.delta;
						}
						else if (e.button == LEFT_MOUSE_BUTTON)
						{
							dragRect.x = mousePosition_initial.x < e.mousePosition.x ? mousePosition_initial.x : e.mousePosition.x;
							dragRect.y = mousePosition_initial.y > e.mousePosition.y ? e.mousePosition.y : mousePosition_initial.y;
							dragRect.width = Mathf.Abs(mousePosition_initial.x - e.mousePosition.x);
							dragRect.height = Mathf.Abs(mousePosition_initial.y - e.mousePosition.y);
						}
						else if (e.alt && e.button == RIGHT_MOUSE_BUTTON)
						{
							SetCanvasScale(uvGraphScale + (e.delta.x - e.delta.y) * ((uvGraphScale / MAX_GRAPH_SCALE_SCROLL) * ALT_SCROLL_MODIFIER));
						}
					}

					break;

				case EventType.Ignore:
				case EventType.MouseUp:

					modifyingUVs_AutoPanel = false;

					if (m_ignore)
					{
						m_ignore = false;
						m_mouseDragging = false;
						m_draggingCanvas = false;
						m_doubleClick = false;
						needsRepaint = true;
						return;
					}

					if (e.button == LEFT_MOUSE_BUTTON && !m_rightMouseDrag && !modifyingUVs && !m_draggingCanvas)
					{
						Vector2 hp = handlePosition;

						if (m_mouseDragging)
						{
							OnMouseDrag();
						}
						else
						{
							UndoUtility.RecordSelection(selection, "Change Selection");

							if (Event.current.modifiers == (EventModifiers)0 && editor)
								editor.ClearElementSelection();

							OnMouseClick(e.mousePosition);

							if (m_doubleClick)
								SelectUVShell();
						}

						if (!e.shift || !userPivot)
							SetHandlePosition(UVSelectionBounds().center, false);
						else
							SetHandlePosition(hp, true);
					}

					if (e.button != RIGHT_MOUSE_BUTTON)
						m_rightMouseDrag = false;

					m_mouseDragging = false;
					m_doubleClick = false;
					m_draggingCanvas = false;

					if (modifyingUVs)
						OnFinishUVModification();

					uvRotation = 0f;
					uvScale = Vector2.one;

					needsRepaint = true;
					break;

				case EventType.ScrollWheel:

					SetCanvasScale(uvGraphScale - e.delta.y * ((uvGraphScale / MAX_GRAPH_SCALE_SCROLL) * SCROLL_MODIFIER));
					e.Use();

					needsRepaint = true;
					break;

				case EventType.ContextClick:

					if (!m_rightMouseDrag)
					{
						var menu = new GenericMenu();
						menu.AddItem(new GUIContent("Selection/Select Island", ""), false, Menu_SelectUVIsland);
						menu.AddItem(new GUIContent("Selection/Select Face", ""), false, Menu_SelectUVFace);
						menu.AddSeparator("");
						AddItemsToMenu(menu);
						menu.ShowAsContext();
					}
					else
						m_rightMouseDrag = false;

					break;

				default:
					return;
			}
		}

		void HandleKeyInput(Event e)
		{
			if (e.type != EventType.KeyUp || eatNextKeyUp)
			{
				eatNextKeyUp = false;
				return;
			}

			bool used = false;

			switch (e.keyCode)
			{
				case KeyCode.Keypad0:
				case KeyCode.Alpha0:
					ResetCanvas();
					uvGraphOffset = Vector2.zero;
					e.Use();
					needsRepaint = true;
					used = true;
					break;

				case KeyCode.Q:
					SetTool_Internal(Tool.View);
					used = true;
					break;

				case KeyCode.W:
					SetTool_Internal(Tool.Move);
					used = true;
					break;

				case KeyCode.E:
					SetTool_Internal(Tool.Rotate);
					used = true;
					break;

				case KeyCode.R:
					SetTool_Internal(Tool.Scale);
					used = true;
					break;

				case KeyCode.F:
					FrameSelection();
					used = true;
					break;
			}

			if (!used && ProBuilderEditor.instance)
				ProBuilderEditor.instance.ShortcutCheck(e);
		}

		/**
		 * Finds the nearest edge to the mouse and sets the `nearestEdge` struct with it's info
		 */
		bool UpdateNearestElement(Vector2 mousePosition)
		{
			if (selection == null || m_mouseDragging || modifyingUVs || tool == Tool.View) // || pb_Handle_Utility.CurrentID > -1)
			{
				if (nearestElement.valid)
				{
					nearestElement.valid = false;
					return true;
				}
				else
				{
					return false;
				}
			}

			Vector2 mpos = GUIToUVPoint(mousePosition);
			Vector2[] uv;
			Vector2 x, y;
			ObjectElementIndex oei = nearestElement;
			nearestElement.valid = false;

			switch (ProBuilderEditor.selectMode)
			{
				case SelectMode.Edge:
					float dist, best = 100f;

					try
					{
						for (int i = 0; i < selection.Length; i++)
						{
							ProBuilderMesh pb = selection[i];
							uv = pb.texturesInternal;

							for (int n = 0; n < pb.facesInternal.Length; n++)
							{
								for (int p = 0; p < pb.facesInternal[n].edgesInternal.Length; p++)
								{
									x = uv[pb.facesInternal[n].edgesInternal[p].a];
									y = uv[pb.facesInternal[n].edgesInternal[p].b];

									dist = Math.DistancePointLineSegment(mpos, x, y);

									if (dist < best)
									{
										nearestElement.objectIndex = i;
										nearestElement.elementIndex = n;
										nearestElement.elementSubIndex = p;
										best = dist;
									}
								}
							}
						}
					}
					catch { }

					nearestElement.valid = best < MIN_DIST_MOUSE_EDGE;
					break;

				case SelectMode.Face:

					try
					{
						bool superBreak = false;
						for (int i = 0; i < selection.Length; i++)
						{
							uv = selection[i].texturesInternal;

							for (int n = 0; n < selection[i].facesInternal.Length; n++)
							{
								if (Math.PointInPolygon(uv, mpos, selection[i].facesInternal[n].edgesInternal.AllTriangles()))
								{
									nearestElement.objectIndex = i;
									nearestElement.elementIndex = n;
									nearestElement.elementSubIndex = -1;
									nearestElement.valid = true;
									superBreak = true;
									break;
								}

								if (superBreak)
									break;
							}
						}
					}
					catch { }

					break;
			}

			return !nearestElement.Equals(oei);
		}

		/**
		 * Allows another window to set the current tool.
		 * Does *not* update any other editor windows.
		 */
		public void SetTool(Tool tool)
		{
			this.tool = tool;
			nearestElement.Clear();
			Repaint();
		}

		/**
		 * Sets the global Tool.current and updates any other windows.
		 */
		private void SetTool_Internal(Tool tool)
		{
			SetTool(tool);

			if (tool == Tool.View)
				Tools.current = Tool.View;
			else
				Tools.current = Tool.None;

			if (editor)
			{
				editor.SetTool(tool);
				SceneView.RepaintAll();
			}
		}

		void OnMouseClick(Vector2 mousePosition)
		{
			if (selection == null)
				return;

			switch (ProBuilderEditor.selectMode)
			{
				case SelectMode.Edge:
					if (nearestElement.valid)
					{
						ProBuilderMesh mesh = selection[nearestElement.objectIndex];

						Edge edge = mesh.facesInternal[nearestElement.elementIndex].edgesInternal[nearestElement.elementSubIndex];
						int ind = mesh.IndexOf(mesh.selectedEdges, edge);

						if (ind > -1)
							mesh.SetSelectedEdges(mesh.selectedEdges.ToArray().RemoveAt(ind));
						else
							mesh.SetSelectedEdges(mesh.selectedEdges.ToArray().Add(edge));
					}

					break;

				case SelectMode.Face:

					Vector2 mpos = GUIToUVPoint(mousePosition);
					bool superBreak = false;
					for (int i = 0; i < selection.Length; i++)
					{
						HashSet<Face> selectedFaces = new HashSet<Face>(selection[i].selectedFacesInternal);

						for (int n = 0; n < selection[i].facesInternal.Length; n++)
						{
							if (Math.PointInPolygon(selection[i].texturesInternal, mpos, selection[i].facesInternal[n].edgesInternal.AllTriangles()))
							{
								if (selectedFaces.Contains(selection[i].facesInternal[n]))
									selectedFaces.Remove(selection[i].facesInternal[n]);
								else
									selectedFaces.Add(selection[i].facesInternal[n]);

								// Only select one face per click
								superBreak = true;
								break;
							}
						}

						selection[i].SetSelectedFaces(selectedFaces.ToArray());

						if (superBreak)
							break;
					}

					break;

				case SelectMode.Vertex:
					RefreshUVCoordinates(new Rect(mousePosition.x - 8, mousePosition.y - 8, 16, 16), true);
					break;
			}

			if (editor)
			{
				ProBuilderEditor.Refresh();
				SceneView.RepaintAll();
			}
			else
			{
				RefreshSelectedUVCoordinates();
			}
		}

		void OnMouseDrag()
		{
			Event e = Event.current;

			if (editor && !e.shift && !e.control && !e.command)
			{
				UndoUtility.RecordSelection(selection, "Change Selection");
				editor.ClearElementSelection();
			}

			RefreshUVCoordinates(dragRect, false);
			e.Use();
		}
#endregion
#region Tools

		// tool properties
		float uvRotation = 0f;
		Vector2 uvOrigin = Vector2.zero;

		Vector2[][] uv_origins = null;
		Vector2 handlePosition = Vector2.zero,
			handlePosition_origin = Vector2.zero,
			handlePosition_offset = Vector2.zero;

		/**
		 * Draw an interactive 2d Move tool that affects the current selection of UV coordinates.
		 */
		void MoveTool()
		{
			Event e = Event.current;

			Vector2 t_handlePosition = UVToGUIPoint(handlePosition);

			EditorHandleUtility.limitToLeftButton = false; // enable right click drag
			t_handlePosition = EditorHandleUtility.PositionHandle2d(1, t_handlePosition, HANDLE_SIZE);
			t_handlePosition = GUIToUVPoint(t_handlePosition);
			EditorHandleUtility.limitToLeftButton = true;

			if (!e.isMouse)
				return;

			/**
			 *	Setting a custom pivot
			 */
			if ((e.button == RIGHT_MOUSE_BUTTON || (e.alt && e.button == LEFT_MOUSE_BUTTON)) && !Math.Approx2(t_handlePosition, handlePosition, .0001f))
			{
				userPivot = true; // flag the handle as having been user set.

				if (ControlKey)
				{
					handlePosition = Snapping.SnapValue(t_handlePosition, (Vector3) new Vector3Mask((handlePosition - t_handlePosition), Math.handleEpsilon) * s_GridSnapIncrement);
				}
				else
				{
					handlePosition = t_handlePosition;

					/**
					 * Attempt vertex proximity snap if shift key is held
					 */
					if (ShiftKey)
					{
						float dist, minDist = MAX_PROXIMITY_SNAP_DIST_CANVAS;
						Vector2 offset = Vector2.zero;
						for (int i = 0; i < selection.Length; i++)
						{
							// todo reset MAX_PROXIMITY_SNAP_DIST
							int index = EditorHandleUtility.NearestPoint(handlePosition, selection[i].texturesInternal, MAX_PROXIMITY_SNAP_DIST_CANVAS);

							if (index < 0)
								continue;

							dist = Vector2.Distance(selection[i].texturesInternal[index], handlePosition);

							if (dist < minDist)
							{
								minDist = dist;
								offset = selection[i].texturesInternal[index] - handlePosition;
							}
						}

						handlePosition += offset;
					}
				}

				SetHandlePosition(handlePosition, true);

				return;
			}

			/**
			 *	Tool activated - moving some UVs around.
			 * 	Unlike rotate and scale tools, if the selected faces are Auto the pb_UV changes will be applied
			 *	in OnFinishUVModification, not at real time.
			 */
			if (!Math.Approx2(t_handlePosition, handlePosition, Math.handleEpsilon))
			{
				// Start of move UV operation
				if (!modifyingUVs)
				{
					// if auto uvs, the changes are applied after action is complete
					if (mode != UVMode.Auto)
						UndoUtility.RegisterCompleteObjectUndo(selection, "Translate UVs");

					handlePosition_origin = handlePosition;
					OnBeginUVModification();
				}

				needsRepaint = true;

				Vector2 newUVPosition = t_handlePosition;

				if (ControlKey)
					newUVPosition = Snapping.SnapValue(newUVPosition, new Vector3Mask((handlePosition - t_handlePosition), Math.handleEpsilon) * s_GridSnapIncrement);

				for (int n = 0; n < selection.Length; n++)
				{
					ProBuilderMesh pb = selection[n];
					Vector2[] uvs = UVEditing.GetUVs(pb, channel);

					foreach (int i in m_DistinctIndexesSelection[n])
						uvs[i] = newUVPosition - (uvOrigin - uv_origins[n][i]);

					// set uv positions before figuring snap dist stuff
					UVEditing.ApplyUVs(pb, uvs, channel, (!ShiftKey || ControlKey) && channel == 0);
				}

				// Proximity snapping
				if (ShiftKey && !ControlKey)
				{
					Vector2 nearestDelta = Vector2.one;

					for (int i = 0; i < selection.Length; i++)
					{
						Vector2[] sel = UnityEngine.ProBuilder.ArrayUtility.ValuesWithIndexes(UVEditing.GetUVs(selection[i], channel), m_DistinctIndexesSelection[i]);

						for (int n = 0; n < selection.Length; n++)
						{
							Vector2 offset;
							if (EditorHandleUtility.NearestPointDelta(sel, UVEditing.GetUVs(selection[n], channel), i == n ? m_DistinctIndexesSelection[i] : null, MAX_PROXIMITY_SNAP_DIST_UV, out offset))
							{
								if (EditorHandleUtility.CurrentAxisConstraint.Mask(offset).sqrMagnitude < nearestDelta.sqrMagnitude)
									nearestDelta = offset;
							}
						}
					}

					if (nearestDelta.sqrMagnitude < .003f)
					{
						nearestDelta = EditorHandleUtility.CurrentAxisConstraint.Mask(nearestDelta);

						for (int i = 0; i < selection.Length; i++)
						{
							Vector2[] uvs = UVEditing.GetUVs(selection[i], channel);

							foreach (int n in m_DistinctIndexesSelection[i])
								uvs[n] += nearestDelta;

							UVEditing.ApplyUVs(selection[i], uvs, channel);
						}

						handlePosition = newUVPosition + nearestDelta;
					}
					else
					{
						if (channel == 0)
						{
							for (int i = 0; i < selection.Length; i++)
							{
								selection[i].mesh.uv = selection[i].texturesInternal;
							}
						}
					}
				}

				RefreshSelectedUVCoordinates();
			}
		}

		private static readonly Vector3 Vec3_Zero = Vector3.zero;

		internal void SceneMoveTool(Vector2 delta)
		{
			/**
			 *	Tool activated - moving some UVs around.
			 * 	Unlike rotate and scale tools, if the selected faces are Auto the pb_UV changes will be applied
			 *	in OnFinishUVModification, not at real time.
			 */
			if (!Math.Approx2(delta, Vec3_Zero, .000001f))
			{
				// Start of move UV operation
				if (!modifyingUVs)
				{
					UndoUtility.RecordSelection(selection, "Move UVs");
					OnBeginUVModification();
					uvOrigin = handlePosition; // have to set this one special
					handlePosition_origin = handlePosition;
				}

				handlePosition.x += delta.x;
				handlePosition.y += delta.y;

				if (ControlKey)
					handlePosition = Snapping.SnapValue(handlePosition, new Vector3Mask((handlePosition - handlePosition), Math.handleEpsilon) * s_GridSnapIncrement);

				for (int n = 0; n < selection.Length; n++)
				{
					ProBuilderMesh pb = selection[n];
					Vector2[] uvs = UVEditing.GetUVs(pb, channel);

					foreach (int i in m_DistinctIndexesSelection[n])
						uvs[i] += delta;

					UVEditing.ApplyUVs(pb, uvs, channel);
				}

				RefreshSelectedUVCoordinates();
			}
		}

		void RotateTool()
		{
			float t_uvRotation = uvRotation;

			uvRotation = EditorHandleUtility.RotationHandle2d(0, UVToGUIPoint(handlePosition), uvRotation, 128);

			if (!Math.Approx(uvRotation, t_uvRotation))
			{
				if (!modifyingUVs)
				{
					UndoUtility.RecordSelection(selection, "Rotate UVs");
					OnBeginUVModification();
				}

				if (ControlKey)
					uvRotation = Snapping.SnapValue(uvRotation, 15f);

				// Do rotation around the handle pivot in manual mode
				if (mode == UVMode.Mixed || mode == UVMode.Manual)
				{
					for (int n = 0; n < selection.Length; n++)
					{
						ProBuilderMesh pb = selection[n];
						Vector2[] uvs = UVEditing.GetUVs(pb, channel);

						foreach (int i in m_DistinctIndexesSelection[n])
							uvs[i] = uv_origins[n][i].RotateAroundPoint(uvOrigin, uvRotation);

						UVEditing.ApplyUVs(pb, uvs, channel);
					}
				}

				// Then apply per-face rotation for auto mode
				if (mode == UVMode.Mixed || mode == UVMode.Auto)
				{
					for (int n = 0; n < selection.Length; n++)
					{
						Face[] autoFaces = System.Array.FindAll(selection[n].selectedFacesInternal, x => !x.manualUV);

						foreach (Face face in autoFaces)
						{
							var uv = face.uv;
							uv.rotation += uvRotation - t_uvRotation;
							face.uv = uv;
						}

						selection[n].RefreshUV(autoFaces);
					}

					RefreshSelectedUVCoordinates();
				}

				nearestElement.valid = false;
			}

			needsRepaint = true;
		}

		internal void SceneRotateTool(float rotation)
		{
			if (rotation != uvRotation)
			{
				if (ControlKey)
					rotation = Snapping.SnapValue(rotation, 15f);

				float delta = rotation - uvRotation;
				uvRotation = rotation;

				if (!modifyingUVs)
				{
					UndoUtility.RecordSelection(selection, "Rotate UVs");
					OnBeginUVModification();
					delta = 0f;
				}

				// Do rotation around the handle pivot in manual mode
				if (mode == UVMode.Mixed || mode == UVMode.Manual)
				{
					for (int n = 0; n < selection.Length; n++)
					{
						ProBuilderMesh pb = selection[n];
						Vector2[] uvs = UVEditing.GetUVs(pb, channel);

						foreach (int i in m_DistinctIndexesSelection[n])
							uvs[i] = uv_origins[n][i].RotateAroundPoint(uvOrigin, uvRotation);

						UVEditing.ApplyUVs(pb, uvs, channel);
					}
				}

				// Then apply per-face rotation for auto mode
				if (mode == UVMode.Mixed || mode == UVMode.Auto)
				{
					for (int n = 0; n < selection.Length; n++)
					{
						Face[] autoFaces = System.Array.FindAll(selection[n].selectedFacesInternal, x => !x.manualUV);

						foreach (Face face in autoFaces)
						{
							var uv = face.uv;
							uv.rotation += delta;
							face.uv = uv;
						}

						selection[n].RefreshUV(autoFaces);
					}

					RefreshSelectedUVCoordinates();
				}

				nearestElement.valid = false;
			}
		}

		Vector2 uvScale = Vector2.one;

		void ScaleTool()
		{
			Vector2 t_uvScale = uvScale;
			uvScale = EditorHandleUtility.ScaleHandle2d(2, UVToGUIPoint(handlePosition), uvScale, 128);

			if (ControlKey)
				uvScale = Snapping.SnapValue(uvScale, s_GridSnapIncrement);

			if (Math.Approx(uvScale.x, 0f, Mathf.Epsilon))
				uvScale.x = .0001f;
			if (Math.Approx(uvScale.y, 0f, Mathf.Epsilon))
				uvScale.y = .0001f;

			if (t_uvScale != uvScale)
			{
				if (!modifyingUVs)
				{
					UndoUtility.RecordSelection(selection, "Scale UVs");
					OnBeginUVModification();
				}

				if (mode == UVMode.Mixed || mode == UVMode.Manual)
				{
					for (int n = 0; n < selection.Length; n++)
					{
						ProBuilderMesh pb = selection[n];
						Vector2[] uvs = UVEditing.GetUVs(pb, channel);

						foreach (int i in m_DistinctIndexesSelection[n])
						{
							uvs[i] = uv_origins[n][i].ScaleAroundPoint(uvOrigin, uvScale);
						}

						UVEditing.ApplyUVs(pb, uvs, channel);
					}
				}

				/**
				 * Auto mode scales UVs prior to rotation, so we have to do it separately here.
				 */
				if (mode == UVMode.Mixed || mode == UVMode.Auto)
				{
					Vector2 scale = uvScale.DivideBy(t_uvScale);
					for (int n = 0; n < selection.Length; n++)
					{
						Face[] autoFaces = System.Array.FindAll(selection[n].selectedFacesInternal, x => !x.manualUV);
						foreach (Face face in autoFaces)
						{
							var uv = face.uv;
							uv.scale = Vector2.Scale(face.uv.scale, scale);
							face.uv = uv;
						}

						selection[n].RefreshUV(autoFaces);
					}

					RefreshSelectedUVCoordinates();
				}

				nearestElement.valid = false;
				needsRepaint = true;
			}
		}

		/**
		 * New scale, previous scale
		 */
		internal void SceneScaleTool(Vector2 textureScale, Vector2 previousScale)
		{
			textureScale.x = 1f / textureScale.x;
			textureScale.y = 1f / textureScale.y;

			previousScale.x = 1f / previousScale.x;
			previousScale.y = 1f / previousScale.y;

			if (ControlKey)
				textureScale = Snapping.SnapValue(textureScale, s_GridSnapIncrement);

			if (!modifyingUVs)
			{
				UndoUtility.RecordSelection(selection, "Scale UVs");
				OnBeginUVModification();
			}

			if (mode == UVMode.Mixed || mode == UVMode.Manual)
			{
				for (int n = 0; n < selection.Length; n++)
				{
					ProBuilderMesh pb = selection[n];
					Vector2[] uvs = UVEditing.GetUVs(pb, channel);

					foreach (int i in m_DistinctIndexesSelection[n])
					{
						uvs[i] = uv_origins[n][i].ScaleAroundPoint(uvOrigin, textureScale);
					}

					UVEditing.ApplyUVs(pb, uvs, channel);
				}
			}

			// Auto mode scales UVs prior to rotation, so we have to do it separately here.
			if (mode == UVMode.Mixed || mode == UVMode.Auto)
			{
				Vector2 delta = textureScale.DivideBy(previousScale);

				for (int n = 0; n < selection.Length; n++)
				{
					Face[] autoFaces = System.Array.FindAll(selection[n].selectedFacesInternal, x => !x.manualUV);
					foreach (Face face in autoFaces)
					{
						var uv = face.uv;
						uv.scale = Vector2.Scale(face.uv.scale, delta);
						face.uv = uv;
					}

					selection[n].RefreshUV(autoFaces);
				}

				RefreshSelectedUVCoordinates();
			}

			nearestElement.valid = false;
			needsRepaint = true;
		}
#endregion
#region UV Graph Drawing

		Vector2 UVGraphCenter = Vector2.zero;

		// private class UVGraphCoordinates
		// {
		// Remember that Unity GUI coordinates Y origin is the bottom
		private static Vector2 UpperLeft = new Vector2(0f, -1f);
		private static Vector2 UpperRight = new Vector2(1f, -1f);
		private static Vector2 LowerLeft = new Vector2(0f, 0f);
		private static Vector2 LowerRight = new Vector2(1f, 0f);

		private Rect UVGraphZeroZero = new Rect(0, 0, 40, 40);
		private Rect UVGraphOneOne = new Rect(0, 0, 40, 40);

		/**
		 * Must be called inside GL immediate mode context
		 */
		internal void DrawUVGrid(Color gridColor)
		{
			Color col = GUI.color;
			gridColor.a = .1f;

			if (Event.current.type == EventType.Repaint)
			{
				GL.PushMatrix();
				EditorHandleUtility.handleMaterial.SetPass(0);
				GL.MultMatrix(Handles.matrix);

				GL.Begin(GL.LINES);
				GL.Color(gridColor);

				// Grid temp vars
				int GridLines = 64;
				float StepSize = s_GridSnapIncrement; // In UV coordinates

				// Exponentially scale grid size
				while (StepSize * uvGridSize * uvGraphScale < uvGridSize / 10)
					StepSize *= 2f;

				// Calculate what offset the grid should be (different from uvGraphOffset in that we always want to render the grid)
				Vector2 gridOffset = uvGraphOffset;
				gridOffset.x = gridOffset.x % (StepSize * uvGridSize * uvGraphScale); // (uvGridSize * uvGraphScale);
				gridOffset.y = gridOffset.y % (StepSize * uvGridSize * uvGraphScale); // (uvGridSize * uvGraphScale);

				Vector2 p0 = Vector2.zero, p1 = Vector2.zero;

				///==== X axis lines
				p0.x = ((StepSize * (GridLines / 2) * uvGridSize) * uvGraphScale) + UVGraphCenter.x + gridOffset.x;
				p1.x = ((-StepSize * (GridLines / 2) * uvGridSize) * uvGraphScale) + UVGraphCenter.x + gridOffset.x;

				for (int i = 0; i < GridLines + 1; i++)
				{
					p0.y = (((StepSize * i) - ((GridLines * StepSize) / 2)) * uvGridSize) * uvGraphScale + UVGraphCenter.y + gridOffset.y;
					p1.y = p0.y;

					GL.Vertex(p0);
					GL.Vertex(p1);
				}

				///==== Y axis lines
				p0.y = ((StepSize * (GridLines / 2) * uvGridSize) * uvGraphScale) + UVGraphCenter.y + gridOffset.y;
				p1.y = ((-StepSize * (GridLines / 2) * uvGridSize) * uvGraphScale) + UVGraphCenter.y + gridOffset.y;

				for (int i = 0; i < GridLines + 1; i++)
				{
					p0.x = (((StepSize * i) - ((GridLines * StepSize) / 2)) * uvGridSize) * uvGraphScale + UVGraphCenter.x + gridOffset.x;
					p1.x = p0.x;

					GL.Vertex(p0);
					GL.Vertex(p1);
				}

				// Box
				if (screenshotStatus == ScreenshotStatus.Done)
				{
					GL.Color(Color.gray);

					GL.Vertex(UVGraphCenter + (UpperLeft * uvGridSize) * uvGraphScale + uvGraphOffset);
					GL.Vertex(UVGraphCenter + (UpperRight * uvGridSize) * uvGraphScale + uvGraphOffset);

					GL.Vertex(UVGraphCenter + (UpperRight * uvGridSize) * uvGraphScale + uvGraphOffset);
					GL.Vertex(UVGraphCenter + (LowerRight * uvGridSize) * uvGraphScale + uvGraphOffset);

					GL.Color(PreferenceKeys.proBuilderBlue);

					GL.Vertex(UVGraphCenter + (LowerRight * uvGridSize) * uvGraphScale + uvGraphOffset);
					GL.Vertex(UVGraphCenter + (LowerLeft * uvGridSize) * uvGraphScale + uvGraphOffset);

					GL.Vertex(UVGraphCenter + (LowerLeft * uvGridSize) * uvGraphScale + uvGraphOffset);
					GL.Vertex(UVGraphCenter + (UpperLeft * uvGridSize) * uvGraphScale + uvGraphOffset);
				}

				GL.End();
				GL.PopMatrix(); // Pop pop!
			}

			GUI.color = gridColor;

			UVGraphZeroZero.x = UVRectIdentity.x + 4;
			UVGraphZeroZero.y = UVRectIdentity.y + UVRectIdentity.height + 1;

			UVGraphOneOne.x = UVRectIdentity.x + UVRectIdentity.width + 4;
			UVGraphOneOne.y = UVRectIdentity.y;

			Handles.BeginGUI();
			GUI.Label(UVGraphZeroZero, "0, 0");
			GUI.Label(UVGraphOneOne, "1, 1");
			Handles.EndGUI();

			GUI.color = col;
		}

		Rect UVRectIdentity = new Rect(0, 0, 1, 1);

		// re-usable rect for drawing graphs
		Rect r = new Rect(0, 0, 0, 0);

		internal static Texture2D GetMainTexture(Material material)
		{
			if (material == null || material.shader == null)
				return null;

			Texture2D best = null;

			for (int i = 0; i < ShaderUtil.GetPropertyCount(material.shader); i++)
			{
				if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
				{
					string propertyName = ShaderUtil.GetPropertyName(material.shader, i);

					Texture2D tex = material.GetTexture(propertyName) as Texture2D;

					if (tex != null)
					{
						if (propertyName.Contains("_MainTex") || propertyName.Contains("Albedo"))
							return tex;
						else if (best == null)
							best = tex;
					}
				}
			}

			return best;
		}

		private void DrawUVGraph(Rect rect)
		{
			var evt = Event.current;

			UVGraphCenter = rect.center;

			UVRectIdentity.width = uvGridSize * uvGraphScale;
			UVRectIdentity.height = UVRectIdentity.width;

			UVRectIdentity.x = UVGraphCenter.x + uvGraphOffset.x;
			UVRectIdentity.y = UVGraphCenter.y + uvGraphOffset.y - UVRectIdentity.height;

			var texture = GetMainTexture(m_PreviewMaterial);

			if (m_ShowPreviewMaterial && m_PreviewMaterial && texture != null)
				EditorGUI.DrawPreviewTexture(UVRectIdentity, texture, null, ScaleMode.StretchToFill, 0);

			if ((screenshotStatus != ScreenshotStatus.PrepareCanvas && screenshotStatus != ScreenshotStatus.CanvasReady) || !screenshot_hideGrid)
			{
				DrawUVGrid(GridColorPrimary);
			}

			if (selection == null || selection.Length < 1)
				return;

			// Draw regular old outlines
			Vector2 p = Vector2.zero;
			Vector2[] uv;
			r.width = DOT_SIZE;
			r.height = DOT_SIZE;

			// Draw all vertices if in vertex mode
			if (ProBuilderEditor.selectMode == SelectMode.Vertex && screenshotStatus == ScreenshotStatus.Done)
			{
				for (int i = 0; i < selection.Length; i++)
				{
					uv = UVEditing.GetUVs(selection[i], channel);

					if (uv == null)
						continue;

					GUI.color = UVColorSecondary;
					for (int n = 0; n < uv.Length; n++)
					{
						p = UVToGUIPoint(uv[n]);
						r.x = p.x - HALF_DOT;
						r.y = p.y - HALF_DOT;
						GUI.DrawTexture(r, dot, ScaleMode.ScaleToFit);
					}

					GUI.color = UVColorPrimary;

					if (channel < 1)
					{
						foreach (int index in selection[i].selectedIndexesInternal)
						{
							p = UVToGUIPoint(uv[index]);
							r.x = p.x - HALF_DOT;
							r.y = p.y - HALF_DOT;
							GUI.DrawTexture(r, dot, ScaleMode.ScaleToFit);
						}
					}
				}
			}

			Handles.color = UVColorGroupIndicator;

			foreach (List<Vector2> lines in incompleteTextureGroupsInSelection_CoordCache)
				for (int i = 1; i < lines.Count; i++)
					Handles.CircleHandleCap(-1, UVToGUIPoint(lines[i]), Quaternion.identity, 8f, evt.type);

#if PB_DEBUG
		if(debug_showCoordinates)
		{
			Handles.BeginGUI();
			r.width = 256f;
			r.height = 40f;
			foreach(pb_Object pb in selection)
			{
				foreach(int i in pb.SelectedTriangles)
				{
					Vector2 v = pb.uv[i];
					Vector2 sv = UVToGUIPoint(v);
					r.x = sv.x;
					r.y = sv.y;
					GUI.Label(r, "UV:" + v.ToString("F2") + "\nScreen: " + (int)sv.x + ", " + (int)sv.y);
				}
			}
			Handles.EndGUI();
		}
		#endif

			GUI.color = Color.white;

			if (evt.type == EventType.Repaint)
			{
				GL.PushMatrix();
				EditorHandleUtility.handleMaterial.SetPass(0);
				GL.MultMatrix(Handles.matrix);

				/**
				 * Draw incomplete texture group indicators (unless taking a screenshot)
				 */
				if (screenshotStatus == ScreenshotStatus.Done)
				{
					GL.Begin(GL.LINES);
					GL.Color(UVColorGroupIndicator);

					foreach (List<Vector2> lines in incompleteTextureGroupsInSelection_CoordCache)
					{
						Vector2 cen = lines[0];

						for (int i = 1; i < lines.Count; i++)
						{
							GL.Vertex(UVToGUIPoint(cen));
							GL.Vertex(UVToGUIPoint(lines[i]));
						}
					}

					GL.End();
				}

				GL.Begin(GL.LINES);

				if (screenshotStatus != ScreenshotStatus.Done)
					GL.Color(screenshot_lineColor);
				else
					GL.Color(UVColorSecondary);

				Vector2 x = Vector2.zero, y = Vector2.zero;

				if (channel == 0)
				{
					for (int i = 0; i < selection.Length; i++)
					{
						ProBuilderMesh pb = selection[i];
						uv = pb.texturesInternal;

						for (int n = 0; n < pb.facesInternal.Length; n++)
						{
							Face face = pb.facesInternal[n];

							foreach (Edge edge in face.edgesInternal)
							{
								x = UVToGUIPoint(uv[edge.a]);
								y = UVToGUIPoint(uv[edge.b]);

								GL.Vertex3(x.x, x.y, 0f);
								GL.Vertex3(y.x, y.y, 0f);
							}
						}
					}
				}
				else
				{
					Vector2 z = Vector2.zero;

					for (int i = 0; i < selection.Length; i++)
					{
						uv = UVEditing.GetUVs(selection[i], channel);

						if (uv == null || uv.Length != selection[i].mesh.vertexCount)
							continue;

						int[] triangles = selection[i].mesh.triangles;

						for (int n = 0; n < triangles.Length; n += 3)
						{
							x = UVToGUIPoint(uv[triangles[n]]);
							y = UVToGUIPoint(uv[triangles[n + 1]]);
							z = UVToGUIPoint(uv[triangles[n + 2]]);

							GL.Vertex3(x.x, x.y, 0f);
							GL.Vertex3(y.x, y.y, 0f);

							GL.Vertex3(y.x, y.y, 0f);
							GL.Vertex3(z.x, z.y, 0f);

							GL.Vertex3(z.x, z.y, 0f);
							GL.Vertex3(x.x, x.y, 0f);
						}
					}
				}

				GL.End();

				/**
				 * Draw selected UVs with shiny green color and dots
				 */
				if (screenshotStatus != ScreenshotStatus.Done)
				{
					GL.PopMatrix();
					return;
				}

				// If in read-only mode (anything other than UV0) don't render selection stuff
				if (channel == 0)
				{
					GL.Begin(GL.LINES);
					GL.Color(UVColorPrimary);

					for (int i = 0; i < selection.Length; i++)
					{
						ProBuilderMesh pb = selection[i];
						uv = pb.texturesInternal;

						if (pb.selectedEdgeCount > 0)
						{
							foreach (Edge edge in pb.selectedEdges)
							{
								x = UVToGUIPoint(uv[edge.a]);
								y = UVToGUIPoint(uv[edge.b]);

								GL.Vertex3(x.x, x.y, 0f);
								GL.Vertex3(y.x, y.y, 0f);

								// #if PB_DEBUG
								// GUI.Label( new Rect(x.x, x.y, 120, 20), pb.uv[edge.x].ToString() );
								// GUI.Label( new Rect(y.x, y.y, 120, 20), pb.uv[edge.y].ToString() );
								// #endif
							}
						}
					}

					GL.End();

					switch (ProBuilderEditor.selectMode)
					{
						case SelectMode.Edge:

							GL.Begin(GL.LINES);
							GL.Color(Color.red);
							if (nearestElement.valid && nearestElement.elementSubIndex > -1 && !modifyingUVs)
							{
								Edge edge = selection[nearestElement.objectIndex].facesInternal[nearestElement.elementIndex].edgesInternal[nearestElement.elementSubIndex];
								GL.Vertex(UVToGUIPoint(selection[nearestElement.objectIndex].texturesInternal[edge.a]));
								GL.Vertex(UVToGUIPoint(selection[nearestElement.objectIndex].texturesInternal[edge.b]));
							}

							GL.End();

							break;

						case SelectMode.Face:
						{
							Vector3 v = Vector3.zero;

							if (nearestElement.valid && !m_mouseDragging)
							{
								GL.Begin(GL.TRIANGLES);

								GL.Color(selection[nearestElement.objectIndex].facesInternal[nearestElement.elementIndex].manualUV ? HOVER_COLOR_MANUAL : HOVER_COLOR_AUTO);
								int[] tris = selection[nearestElement.objectIndex].facesInternal[nearestElement.elementIndex].indexesInternal;

								for (int i = 0; i < tris.Length; i += 3)
								{
									v = UVToGUIPoint(selection[nearestElement.objectIndex].texturesInternal[tris[i + 0]]);
									GL.Vertex3(v.x, v.y, 0f);
									v = UVToGUIPoint(selection[nearestElement.objectIndex].texturesInternal[tris[i + 1]]);
									GL.Vertex3(v.x, v.y, 0f);
									v = UVToGUIPoint(selection[nearestElement.objectIndex].texturesInternal[tris[i + 2]]);
									GL.Vertex3(v.x, v.y, 0f);
								}

								GL.End();
							}

							GL.Begin(GL.TRIANGLES);
							for (int i = 0; i < selection.Length; i++)
							{
								foreach (Face face in selection[i].selectedFacesInternal)
								{
									GL.Color(face.manualUV ? SELECTED_COLOR_MANUAL : SELECTED_COLOR_AUTO);

									int[] tris = face.indexesInternal;

									for (int n = 0; n < tris.Length; n += 3)
									{
										v = UVToGUIPoint(selection[i].texturesInternal[tris[n + 0]]);
										GL.Vertex3(v.x, v.y, 0f);
										v = UVToGUIPoint(selection[i].texturesInternal[tris[n + 1]]);
										GL.Vertex3(v.x, v.y, 0f);
										v = UVToGUIPoint(selection[i].texturesInternal[tris[n + 2]]);
										GL.Vertex3(v.x, v.y, 0f);
									}
								}
							}

							GL.End();
						}
							break;
					}
				}

				GL.PopMatrix();
			}
		}

#if PB_DEBUG
	void DrawDebugInfo(Rect rect)
	{
		Vector2 mpos = Event.current.mousePosition;

		GUI.BeginGroup(rect);
		GUILayout.BeginVertical(GUILayout.MaxWidth(rect.width-6));

		GUILayout.Label("Scale: " + uvGraphScale);

		GUILayout.Label("Object: " + nearestElement.ToString());
		GUILayout.Label(mpos + " (" + this.position.width + ", " + this.position.height + ")");

		// GUILayout.Label("m_mouseDragging: " + m_mouseDragging);
		// GUILayout.Label("m_rightMouseDrag: " + m_rightMouseDrag);
		// GUILayout.Label("m_draggingCanvas: " + m_draggingCanvas);
		// GUILayout.Label("modifyingUVs: " + modifyingUVs);

		debug_showCoordinates = EditorGUILayout.Toggle("Show UV coordinates", debug_showCoordinates);

		GUILayout.Label("Handle: " + handlePosition.ToString("F3"));
		GUILayout.Label("Offset: " + handlePosition_offset.ToString("F3"));

		GUI.EndGroup();
	}
	#endif
#endregion
#region UV Canvas Operations

		/**
		 * Zooms in on the current UV selection
		 */
		void FrameSelection()
		{
			needsRepaint = true;

			if (selection == null || selection.Length < 1 || (editor && MeshSelection.selectedVertexCount < 1))
			{
				SetCanvasCenter(Event.current.mousePosition - UVGraphCenter - uvGraphOffset);
				return;
			}

			SetCanvasCenter(selectedGuiBounds.center - uvGraphOffset - UVGraphCenter);

			if (UVSelectionBounds().size.sqrMagnitude > 0f)
			{
				Bounds2D bounds = UVSelectionBounds();

				float x = (float)screenWidth / ((bounds.size.x * uvGridSize) * 1.5f);
				float y = (float)(screenHeight - 96) / ((bounds.size.y * uvGridSize) * 1.5f);

				SetCanvasScale(Mathf.Min(x, y));
			}
		}

		/**
		 * Sets the canvas scale.  1 is full size, .1 is super zoomed, and 2 would be 2x out.
		 */
		void SetCanvasScale(float zoom)
		{
			Vector2 center = -(uvGraphOffset / uvGraphScale);
			uvGraphScale = Mathf.Clamp(zoom, MIN_GRAPH_SCALE, MAX_GRAPH_SCALE);
			SetCanvasCenter(center * uvGraphScale);
		}

		/**
		 * Center the canvas on this point.  Should be in GUI coordinates.
		 */
		void SetCanvasCenter(Vector2 center)
		{
			uvGraphOffset = center;
			uvGraphOffset.x = -uvGraphOffset.x;
			uvGraphOffset.y = -uvGraphOffset.y;
		}

		void ResetCanvas()
		{
			uvGraphScale = 1f;
			SetCanvasCenter(new Vector2(.5f, -.5f) * uvGridSize * uvGraphScale);
		}

		/**
		 * Set the handlePosition to this UV coordinate.
		 */
		bool userPivot = false;

		void SetHandlePosition(Vector2 uvPoint, bool isUserSet)
		{
			if (float.IsNaN(uvPoint.x) || float.IsNaN(uvPoint.y))
				return;

			userPivot = isUserSet;
			handlePosition_offset = UVSelectionBounds().center - uvPoint;
			handlePosition = uvPoint;
		}

		/**
		 * Used by pb_Editor to reset the pivot offset when adding or removing faces in the scenview.
		 */
		public void ResetUserPivot()
		{
			handlePosition_offset = Vector2.zero;
		}

		Bounds2D GetBounds(int i, int f, Vector2[][] array)
		{
			return new Bounds2D(UnityEngine.ProBuilder.ArrayUtility.ValuesWithIndexes(array[i], selection[i].facesInternal[f].distinctIndexesInternal));
		}

		/**
		 * Convert a point on the UV canvas (0,1 scaled to guisize) to a GUI coordinate.
		 */
		Vector2 UVToGUIPoint(Vector2 v)
		{
			Vector2 p = new Vector2(v.x, -v.y);
			return UVGraphCenter + (p * uvGridSize * uvGraphScale) + uvGraphOffset;
		}

		Vector2 GUIToUVPoint(Vector2 v)
		{
			Vector2 p = (v - (UVGraphCenter + uvGraphOffset)) / (uvGraphScale * uvGridSize);
			p.y = -p.y;
			return p;
		}

		Vector3 CanvasToGUIPoint(Vector2 v)
		{
			v.x = UVGraphCenter.x + (v.x * uvGraphScale + uvGraphOffset.x);
			v.y = UVGraphCenter.y + (v.y * uvGraphScale + uvGraphOffset.y);
			return v;
		}

		/**
		 * Convert a mouse position in GUI space to a canvas relative point
		 */
		Vector2 GUIToCanvasPoint(Vector2 v)
		{
			return ((v - UVGraphCenter) - uvGraphOffset) / uvGraphScale;
		}

		private Bounds2D _selected_gui_bounds = new Bounds2D(Vector2.zero, Vector2.zero);

		/**
		 * Returns the bounds of the current selection in GUI space.
		 */
		Bounds2D selectedGuiBounds
		{
			get
			{
				Bounds2D uvBounds = UVSelectionBounds();
				_selected_gui_bounds.center = UVToGUIPoint(uvBounds.center);
				_selected_gui_bounds.size = uvBounds.size * uvGridSize * uvGraphScale;
				return _selected_gui_bounds;
			}
		}

		/// <summary>
		/// Returns the bounds of the current selection in UV space
		/// </summary>
		/// <returns></returns>
		Bounds2D UVSelectionBounds()
		{
			float xMin = 0f, xMax = 0f, yMin = 0f, yMax = 0f;
			bool first = true;
			for (int n = 0; n < selection.Length; n++)
			{
				Vector2[] uv = selection[n].texturesInternal;

				foreach (int i in m_DistinctIndexesSelection[n])
				{
					if (first)
					{
						xMin = uv[i].x;
						xMax = xMin;
						yMin = uv[i].y;
						yMax = yMin;
						first = false;
					}
					else
					{
						xMin = Mathf.Min(xMin, uv[i].x);
						yMin = Mathf.Min(yMin, uv[i].y);

						xMax = Mathf.Max(xMax, uv[i].x);
						yMax = Mathf.Max(yMax, uv[i].y);
					}
				}
			}

			return new Bounds2D(new Vector2((xMin + xMax) / 2f, (yMin + yMax) / 2f), new Vector2(xMax - xMin, yMax - yMin));
		}
#endregion
#region Refresh / Set

		// Doesn't call Repaint for you
		void RefreshUVCoordinates()
		{
			RefreshUVCoordinates(null, false);
		}

		/**
		 * If dragRect is null, the selected UV array will be derived using the selected ProBuilder faces.
		 * If it ain't null, selected UVs will be set to the UV coordinates contained within the drag rect.
		 */
		void RefreshUVCoordinates(Rect? dragRect, bool isClick)
		{
			if (editor == null || selection == null)
				return;

			// Convert dragrect from Unity GUI space to UV coordinates
			Bounds2D dragBounds;

			if (dragRect != null)
				dragBounds = new Bounds2D(GUIToUVPoint(((Rect)dragRect).center), new Vector2(((Rect)dragRect).width, ((Rect)dragRect).height) / (uvGraphScale * uvGridSize));
			else
				dragBounds = new Bounds2D(Vector2.zero, Vector2.zero);

			selectedUVCount = MeshSelection.selectedVertexCount;
			selectedFaceCount = MeshSelection.selectedFaceCount;

			for (int i = 0; i < selection.Length; i++)
			{
				ProBuilderMesh pb = selection[i];

				Vector2[] mshUV = UVEditing.GetUVs(pb, channel);

				// if this is the uv0 channel and the count doesn't match pb vertex count, reset
				if (channel == 0 && (mshUV == null || mshUV.Length != pb.vertexCount || mshUV.Any(x => float.IsNaN(x.x) || float.IsNaN(x.y))))
				{
					mshUV = new Vector2[pb.vertexCount];
					UVEditing.ApplyUVs(pb, mshUV, channel);
				}

				int len = mshUV != null ? mshUV.Length : 0;

				// this should be separate from RefreshUVCoordinates
				if (dragRect != null && channel == 0)
				{
					switch (ProBuilderEditor.selectMode)
					{
						case SelectMode.Vertex:
							List<int> selectedTris = new List<int>(pb.selectedIndexesInternal);

							for (int j = 0; j < len; j++)
							{
								if (dragBounds.ContainsPoint(mshUV[j]))
								{
									int indx = selectedTris.IndexOf(j);

									if (indx > -1)
										selectedTris.RemoveAt(indx);
									else
										selectedTris.Add(j);

									// if this is a click, only do one thing per-click
									if (isClick)
										break;
								}
							}

							pb.SetSelectedVertices(selectedTris.ToArray());
							break;

						case SelectMode.Edge:
							List<Edge> selectedEdges = new List<Edge>(pb.selectedEdges);

							for (int n = 0; n < pb.facesInternal.Length; n++)
							{
								for (int p = 0; p < pb.facesInternal[n].edgesInternal.Length; p++)
								{
									Edge edge = pb.facesInternal[n].edgesInternal[p];

									if (dragBounds.IntersectsLineSegment(mshUV[edge.a], mshUV[edge.b]))
									{
										if (!selectedEdges.Contains(edge))
											selectedEdges.Add(edge);
										else
											selectedEdges.Remove(edge);
									}
								}
							}

							pb.SetSelectedEdges(selectedEdges.ToArray());
							break;

						/**
						 * Check if any of the faces intersect with the mousedrag rect.
						 */
						case SelectMode.Face:

							HashSet<Face> selectedFaces = new HashSet<Face>(selection[i].selectedFacesInternal);

							for (int n = 0; n < pb.facesInternal.Length; n++)
							{
								Face face = pb.facesInternal[n];

								int[] distinctIndexes = pb.facesInternal[n].distinctIndexesInternal;

								bool allPointsContained = true;

								for (int t = 0; t < distinctIndexes.Length; t++)
								{
									if (!dragBounds.ContainsPoint(mshUV[distinctIndexes[t]]))
									{
										allPointsContained = false;
										break;
									}
								}

								// // if(dragBounds.Intersects(faceBounds))
								// for(int t = 0; t < uvs.Length; t++)
								// {
								// 	if(!dragBounds.ContainsPoint(uvs[t]))
								// 	{
								// 		allPointsContained = false;
								// 		break;
								// 	}
								// }

								if (allPointsContained)
								{
									if (selectedFaces.Contains(face))
										selectedFaces.Remove(face);
									else
										selectedFaces.Add(face);
								}
							}

							selection[i].SetSelectedFaces(selectedFaces.ToArray());

							break;
					}

					ProBuilderEditor.Refresh();
					SceneView.RepaintAll();
				}
			}

			// figure out what the mode of selected faces is
			if (MeshSelection.selectedFaceCount > 0)
			{
				// @todo write a more effecient method for this
				List<bool> manual = new List<bool>();
				for (int i = 0; i < selection.Length; i++)
					manual.AddRange(selection[i].selectedFacesInternal.Select(x => x.manualUV).ToList());
				int c = manual.Distinct().Count();
				if (c > 1)
					mode = UVMode.Mixed;
				else if (c > 0)
					mode = manual[0] ? UVMode.Manual : UVMode.Auto;
			}
			else
			{
				mode = UVMode.Manual;
			}

			m_PreviewMaterial = editor.GetFirstSelectedMaterial();

			handlePosition = UVSelectionBounds().center - handlePosition_offset;
		}

		/**
		 * Refresh only the selected UV coordinates.
		 */
		void RefreshSelectedUVCoordinates()
		{
			handlePosition = UVSelectionBounds().center - handlePosition_offset;
		}
#endregion
#region UV Toolbar

		Rect toolbarRect_tool = new Rect(PAD, PAD, 130f, 24f);
		Rect toolbarRect_select = new Rect(PAD + 130 + PAD, PAD, 130f, 24f);

		GUIStyle commandStyle = null;

		void DrawUVTools(Rect rect)
		{
			GUI.BeginGroup(rect);

			if (commandStyle == null)
				commandStyle = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle("Command");

			/**
			 * Handle toggles and SelectionMode toggles.
			 */
			EditorGUI.BeginChangeCheck();

			tool = (Tool)GUI.Toolbar(toolbarRect_tool, (int)tool < 0 ? 0 : (int)tool, ToolIcons, "Command");

			if (EditorGUI.EndChangeCheck())
			{
				SetTool_Internal(tool);
				SceneView.RepaintAll();
			}

			var mode = ProBuilderEditor.selectMode;

			int currentSelectionMode = mode == SelectMode.Vertex ? 0
				: mode == SelectMode.Edge ? 1
				: mode == SelectMode.Face ? 2 : -1;

			GUI.enabled = channel == 0;

			EditorGUI.BeginChangeCheck();
			currentSelectionMode = GUI.Toolbar(toolbarRect_select, currentSelectionMode, SelectionIcons, "Command");
			if (EditorGUI.EndChangeCheck())
			{
				if (currentSelectionMode == 0)
					ProBuilderEditor.selectMode = SelectMode.Vertex;
				else if (currentSelectionMode == 1)
					ProBuilderEditor.selectMode = SelectMode.Edge;
				else if (currentSelectionMode == 2)
					ProBuilderEditor.selectMode = SelectMode.Face;
			}


			// begin Editor pref toggles (Show Texture, Lock UV sceneview handle, etc)

			Rect editor_toggles_rect = new Rect(toolbarRect_select.x + 130, PAD - 1, 36f, 22f);

			if (editor)
			{
				gc_SceneViewUVHandles.image = ProBuilderEditor.selectMode == SelectMode.TextureFace ? icon_sceneUV_on : icon_sceneUV_off;

				if (GUI.Button(editor_toggles_rect, gc_SceneViewUVHandles))
				{
					if (ProBuilderEditor.selectMode == SelectMode.TextureFace)
						ProBuilderEditor.ResetToLastSelectMode();
					else
						ProBuilderEditor.selectMode = SelectMode.TextureFace;
				}
			}

			GUI.enabled = true;

			editor_toggles_rect.x += editor_toggles_rect.width + PAD;

			gc_ShowPreviewTexture.image = m_ShowPreviewMaterial ? icon_textureMode_on : icon_textureMode_off;

			if (GUI.Button(editor_toggles_rect, gc_ShowPreviewTexture))
				m_ShowPreviewMaterial.SetValue(!m_ShowPreviewMaterial, true);

			editor_toggles_rect.x += editor_toggles_rect.width + PAD;

			if (GUI.Button(editor_toggles_rect, gc_RenderUV))
				ScreenshotMenu();

			int t_channel = channel;

			Rect channelRect = new Rect(
				this.position.width - (108 + 8),
				editor_toggles_rect.y + 3,
				108f,
				20f);

			channel = EditorGUI.IntPopup(channelRect, channel, UV_CHANNELS_STR, UV_CHANNELS);

			if (channel != t_channel)
			{
				if (t_channel == 0)
				{
					foreach (ProBuilderMesh pb in selection)
						pb.SetSelectedVertices(new int[0] { });
				}

				RefreshUVCoordinates();
			}

			GUI.EndGroup();
		}

		static Rect ActionWindowDragRect = new Rect(0, 0, 10000, 20);
		static Editor uv2Editor = null;

		void DrawActionWindow(int windowIndex)
		{
			if (channel == 0)
			{
				GUILayout.Label("UV Mode: " + mode.ToString(), EditorStyles.boldLabel);

				switch (mode)
				{
					case UVMode.Auto:
						DrawAutoModeUI();
						break;

					case UVMode.Manual:
						DrawManualModeUI();
						break;

					case UVMode.Mixed:

						if (GUILayout.Button(gc_ConvertToManual, EditorStyles.miniButton))
							Menu_SetManualUV();

						if (GUILayout.Button(gc_ConvertToAuto, EditorStyles.miniButton))
							Menu_SetAutoUV();

						break;
				}
			}
			else if (channel == 1)
			{
				EditorUtility.CreateCachedEditor<UnwrapParametersEditor>(selection, ref uv2Editor);

				if (uv2Editor != null)
				{
					GUILayout.Space(4);
					uv2Editor.hideFlags = HideFlags.HideAndDontSave;
					uv2Editor.OnInspectorGUI();
				}

				GUILayout.FlexibleSpace();

				if (GUILayout.Button("Rebuild Selected UV2"))
				{
					foreach (var mesh in selection)
						mesh.Optimize(true);
				}

				GUILayout.Space(5);
			}

			GUI.DragWindow(ActionWindowDragRect);
			actionWindowRect = UI.EditorGUILayout.DoResizeHandle(actionWindowRect);
		}

		bool modifyingUVs_AutoPanel = false;

		void DrawAutoModeUI()
		{
			if (GUILayout.Button("Convert to Manual", EditorStyles.miniButton))
				Menu_SetManualUV();

			bool isKeyDown = Event.current.type == EventType.KeyDown;

			if (AutoUVEditor.OnGUI(selection, (int)actionWindowRect.width))
			{
				if (!modifyingUVs_AutoPanel)
				{
					modifyingUVs_AutoPanel = true;

					foreach (ProBuilderMesh pb in selection)
					{
						pb.ToMesh();
						pb.Refresh();
					}
				}

				foreach (var kvp in MeshSelection.selectedFacesInEditZone)
					kvp.Key.RefreshUV(kvp.Value);

				RefreshSelectedUVCoordinates();
			}

#if UNITY_2017_3_OR_NEWER
			if (isKeyDown && Event.current.type == EventType.Used)
#else
		if( isKeyDown && Event.current.type == EventType.used )
#endif
				eatNextKeyUp = true;

			GUI.enabled = selectedFaceCount > 0;
		}

		bool tool_weldButton = false;

		Vector2 scroll = Vector2.zero;

		void DrawManualModeUI()
		{
			GUI.enabled = selectedFaceCount > 0;

			if (GUILayout.Button(gc_ConvertToAuto, EditorStyles.miniButton))
				Menu_SetAutoUV();

			scroll = EditorGUILayout.BeginScrollView(scroll);

			/**
			 * Projection Methods
			 */
			GUILayout.Label("Project UVs", EditorStyles.miniBoldLabel);

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Planar", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_PlanarProject();

			if (GUILayout.Button("Box", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_BoxProject();

			GUILayout.EndHorizontal();

			/**
			 * Selection
			 */
			GUI.enabled = selectedUVCount > 0;
			GUILayout.Label("Selection", EditorStyles.miniBoldLabel);

			if (GUILayout.Button("Select Island", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_SelectUVIsland();

			GUI.enabled = selectedUVCount > 0 && ProBuilderEditor.selectMode != SelectMode.Face;
			if (GUILayout.Button("Select Face", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_SelectUVFace();

			/**
			 * Edit
			 */
			GUILayout.Label("Edit", EditorStyles.miniBoldLabel);

			GUI.enabled = selectedUVCount > 1;

			tool_weldButton = UI.EditorGUIUtility.ToolSettingsGUI("Weld", "Merge selected vertices that are within a specified distance of one another.",
				tool_weldButton,
				Menu_SewUVs,
				WeldButtonGUI,
				(int)actionWindowRect.width,
				20,
				selection);

			if (GUILayout.Button("Collapse UVs", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_CollapseUVs();

			GUI.enabled = selectedUVCount > 1;
			if (GUILayout.Button("Split UVs", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_SplitUVs();

			GUILayout.Space(4);

			if (GUILayout.Button("Flip Horizontal", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_FlipUVs(Vector2.up);

			if (GUILayout.Button("Flip Vertical", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_FlipUVs(Vector2.right);

			GUILayout.Space(4);

			if (GUILayout.Button("Fit UVs", EditorStyles.miniButton, GUILayout.MaxWidth(actionWindowRect.width)))
				Menu_FitUVs();

			EditorGUILayout.EndScrollView();

			GUI.enabled = true;
		}

		const float k_MinimumSewUVDistance = .001f;
		Pref<float> m_WeldDistance = new Pref<float>("UVEditor.weldDistance", .01f);

		void WeldButtonGUI(int width)
		{
			EditorGUI.BeginChangeCheck();

			m_WeldDistance.value = EditorGUILayout.FloatField(new GUIContent("Max", "The maximum distance between two vertices in order to be welded together."), m_WeldDistance);

			if (m_WeldDistance <= k_MinimumSewUVDistance)
				m_WeldDistance.value = k_MinimumSewUVDistance;

			if (EditorGUI.EndChangeCheck())
				ProBuilderSettings.Save();
		}
#endregion
#region UV Selection

		/**
		 * Given selected tris, return an array of all indexes attached to face
		 */
		private void SelectUVShell()
		{
			if (selection == null || selection.Length < 1)
				return;

			foreach (ProBuilderMesh pb in selection)
			{
				Face[] faces = GetFaces(pb, pb.selectedIndexesInternal);

				List<int> elementGroups = new List<int>();
				List<int> textureGroups = new List<int>();

				foreach (Face f in faces)
				{
					if (f.manualUV)
						elementGroups.Add(f.elementGroup);
					else
						textureGroups.Add(f.textureGroup);
				}

				IEnumerable<Face> matches = System.Array.FindAll(pb.facesInternal, x =>
					(x.manualUV && x.elementGroup > -1 && elementGroups.Contains(x.elementGroup)) ||
					(!x.manualUV && x.textureGroup > 0 && textureGroups.Contains(x.textureGroup)));

				pb.SetSelectedFaces(faces.Union(matches).ToArray());
				ProBuilderEditor.Refresh();
			}
		}

		/**
		 * If any of the faces in @selection are AutoUV and in a texture group, this
		 * augments the texture group buddies to the selection and returns it.
		 */
		private Face[] SelectTextureGroups(ProBuilderMesh pb, Face[] selection)
		{
			List<int> texGroups = selection.Select(x => x.textureGroup).Where(x => x > 0).Distinct().ToList();
			Face[] sel = System.Array.FindAll(pb.facesInternal, x => !x.manualUV && texGroups.Contains(x.textureGroup));

			return selection.Union(sel).ToArray();
		}

		/**
		 * If selection contains faces that are part of a texture group, and not all of those group faces are in the selection,
		 * return a pb_Face[] of that entire group so that we can show the user some indication of that groupage.
		 */
		private List<Face[]> GetIncompleteTextureGroups(ProBuilderMesh pb, Face[] selection)
		{
			// get distinct list of all selected texture groups
			List<int> groups = selection.Select(x => x.textureGroup).Where(x => x > 0).Distinct().ToList();
			List<Face[]> incompleteGroups = new List<Face[]>();

			// figure out how many
			for (int i = 0; i < groups.Count; i++)
			{
				Face[] whole_group = System.Array.FindAll(pb.facesInternal, x => !x.manualUV && groups[i] == x.textureGroup);
				int inSelection = System.Array.FindAll(selection, x => x.textureGroup == groups[i]).Length;

				if (inSelection != whole_group.Length)
					incompleteGroups.Add(whole_group);
			}

			return incompleteGroups;
		}

		/**
		 * Sets the SceneView and UV selection to include any faces with currently selected indexes.
		 */
		private void SelectUVFace()
		{
			if (selection == null || selection.Length < 1)
				return;

			foreach (ProBuilderMesh pb in selection)
			{
				Face[] faces = GetFaces(pb, pb.selectedIndexesInternal);
				pb.SetSelectedFaces(faces);
				ProBuilderEditor.Refresh();
			}
		}

		/**
		 *	Element Groups are used to associate faces that share UV seams.  In this
		 *	way, we can easily select UV shells by grouping all elements as opposed
		 *	to iterating through and checking nearby faces every time.
		 */
		private void RefreshElementGroups(ProBuilderMesh pb)
		{
			foreach (Face f in pb.facesInternal)
				f.elementGroup = -1;

			SharedVertex[] sharedUVs = pb.sharedTextures;

			int eg = 0;
			foreach (SharedVertex sharedVertex in sharedUVs)
			{
				if (sharedVertex.arrayInternal.Length < 2)
					continue;

				Face[] faces = GetFaces(pb, sharedVertex);

				int cur = pb.UnusedElementGroup(eg++);

				foreach (Face f in faces)
				{
					if (f.elementGroup > -1)
					{
						int g = f.elementGroup;

						foreach (Face fin in pb.facesInternal)
							if (fin.elementGroup == g)
								fin.elementGroup = cur;
					}

					f.elementGroup = cur;
				}
			}
		}

		/**
		 * Get all faces that contain any of the passed vertex indexes.
		 */
		Face[] GetFaces(ProBuilderMesh pb, IEnumerable<int> indexes)
		{
			List<Face> faces = new List<Face>();

			foreach (Face f in pb.facesInternal)
			{
				foreach (int i in f.distinctIndexesInternal)
				{
					if (indexes.Contains(i))
					{
						faces.Add(f);
						break;
					}
				}
			}

			return faces.Distinct().ToArray();
		}

		/// <summary>
		/// Finds all faces attached to the current selection and marks the faces as having been manually modified.
		/// </summary>
		/// <param name="mesh"></param>
		void FlagSelectedFacesAsManual(ProBuilderMesh mesh)
		{
			// Mark selected UV faces manualUV flag true
			foreach (Face f in GetFaces(mesh, mesh.selectedIndexesInternal))
			{
				f.textureGroup = -1;
				f.manualUV = true;
			}
		}

		/// <summary>
		/// Creates a copy of each msh.uv array in a jagged array, and stores the average of all points.
		/// </summary>
		/// <param name="uvCopy"></param>
		void CopySelectionUVs(out Vector2[][] uvCopy)
		{
			uvCopy = new Vector2[selection.Length][];
			for (int i = 0; i < selection.Length; i++)
			{
				ProBuilderMesh pb = selection[i];
				uvCopy[i] = new Vector2[pb.vertexCount];
				System.Array.Copy(UVEditing.GetUVs(pb, channel), uvCopy[i], pb.vertexCount);
			}
		}
#endregion
#region Menu Commands

		/// <summary>
		/// Planar project UVs on all selected faces in selection.
		/// </summary>
		void Menu_PlanarProject()
		{
			UndoUtility.RecordSelection(selection, "Planar Project Faces");
			int projected = 0;

			for (int i = 0; i < selection.Length; i++)
			{
				if (selection[i].selectedFacesInternal.Length > 0)
				{
					selection[i].ToMesh(); // Remove UV2 modifications
					UVEditing.SplitUVs(selection[i], selection[i].selectedIndexesInternal);
					UVEditing.ProjectFacesAuto(selection[i], selection[i].selectedFacesInternal, channel);

					foreach (Face f in selection[i].selectedFacesInternal)
						f.manualUV = true;

					RefreshElementGroups(selection[i]);

					projected++;
				}
			}

			SetSelectedUVsWithSceneView();

			if (projected > 0)
			{
				CenterUVsAtPoint(handlePosition);
				ResetUserPivot();
			}

			foreach (ProBuilderMesh pb in selection)
			{
				pb.Refresh();
				pb.Optimize();
			}

			EditorUtility.ShowNotification(this, projected > 0 ? "Planar Project" : "Nothing Selected");

			// Special case
			RefreshUVCoordinates();
			needsRepaint = true;
		}

		/**
		 * Box project all selected faces in selection.
		 */
		public void Menu_BoxProject()
		{
			int p = 0;
			UndoUtility.RegisterCompleteObjectUndo(selection, "Box Project Faces");

			for (int i = 0; i < selection.Length; i++)
			{
				selection[i].ToMesh();

				if (selection[i].selectedFacesInternal.Length > 0)
				{
					UVEditing.ProjectFacesBox(selection[i], selection[i].selectedFacesInternal, channel);
					p++;
				}
			}

			SetSelectedUVsWithSceneView();

			if (p > 0)
			{
				CenterUVsAtPoint(handlePosition);
				ResetUserPivot();
			}

			foreach (ProBuilderMesh pb in selection)
			{
				pb.Refresh();
				pb.Optimize();
			}

			EditorUtility.ShowNotification(this, "Box Project UVs");

			// Special case
			RefreshUVCoordinates();
			needsRepaint = true;
		}

		/**
		 * Spherically project all selected indexes in selection.
		 */
		public void Menu_SphericalProject()
		{
			int p = 0;
			UndoUtility.RegisterCompleteObjectUndo(selection, "Spherical Project UVs");

			for (int i = 0; i < selection.Length; i++)
			{
				selection[i].ToMesh();

				if (selection[i].selectedVertexCount > 0)
				{
					UVEditing.ProjectFacesSphere(selection[i], selection[i].selectedIndexesInternal, channel);
					p++;
				}
			}

			SetSelectedUVsWithSceneView();

			if (p > 0)
			{
				CenterUVsAtPoint(handlePosition);
				ResetUserPivot();
			}

			foreach (ProBuilderMesh pb in selection)
			{
				pb.Refresh();
				pb.Optimize();
			}

			EditorUtility.ShowNotification(this, "Spherical Project UVs");

			// Special case
			RefreshUVCoordinates();
			needsRepaint = true;
		}

		/**
		 * Reset all selected faces to use the default Automatic unwrapping.  Removes
		 * any modifications made by the user.
		 */
		public void Menu_SetAutoUV()
		{
			SetIsManual(false);
		}

		/**
		 * Sets all faces to manual UV mode.
		 */
		public void Menu_SetManualUV()
		{
			SetIsManual(true);
		}

		public void SetIsManual(bool isManual)
		{
			UndoUtility.RegisterCompleteObjectUndo(selection, isManual ? "Set Faces Manual" : "Set Faces Auto");

			foreach (ProBuilderMesh pb in selection)
			{
				pb.ToMesh();
				UVEditing.SetAutoUV(pb, pb.selectedFacesInternal, !isManual);
				pb.Refresh();
				pb.Optimize();
			}

			SetSelectedUVsWithSceneView();
			RefreshUVCoordinates();

			EditorUtility.ShowNotification(this, "Set " + selectedFaceCount + " Faces " + (isManual ? "Manual" : "Auto"));
		}

		public void Menu_SelectUVIsland()
		{
			UndoUtility.RecordSelection(selection, "Select Island");

			SelectUVShell();
			EditorUtility.ShowNotification(this, "Select UV Island");
		}

		public void Menu_SelectUVFace()
		{
			UndoUtility.RecordSelection(selection, "Select Face");

			SelectUVFace();
			EditorUtility.ShowNotification(this, "Select UV Face");
		}

		public void Menu_CollapseUVs()
		{
			if (channel == 1)
			{
				EditorUtility.ShowNotification(this, "Invalid UV2 Operation");
				return;
			}

			UndoUtility.RecordSelection(selection, "Collapse UVs");

			for (int i = 0; i < selection.Length; i++)
			{
				selection[i].ToMesh();

				selection[i].CollapseUVs(m_DistinctIndexesSelection[i]);

				selection[i].Refresh();
				selection[i].Optimize();
			}

			RefreshSelectedUVCoordinates();

			EditorUtility.ShowNotification(this, "Collapse UVs");
		}

		public ActionResult Menu_SewUVs(ProBuilderMesh[] selection)
		{
			if (channel == 1)
			{
				EditorUtility.ShowNotification(this, "Invalid UV2 Operation");
				return new ActionResult(ActionResult.Status.Canceled, "Invalid UV2 Operation");
			}

			float weldDistance = m_WeldDistance;

			UndoUtility.RecordSelection(selection, "Sew UV Seams");
			for (int i = 0; i < selection.Length; i++)
			{
				selection[i].ToMesh();

				selection[i].SewUVs(m_DistinctIndexesSelection[i], weldDistance);
				RefreshElementGroups(selection[i]);

				selection[i].Refresh();
				selection[i].Optimize();
			}

			RefreshSelectedUVCoordinates();

			EditorUtility.ShowNotification(this, "Weld UVs");
			return new ActionResult(ActionResult.Status.Success, "Invalid UV2 Operation");
		}

		public void Menu_SplitUVs()
		{
			if (channel == 1)
			{
				EditorUtility.ShowNotification(this, "Invalid UV2 Operation");
				return;
			}

			UndoUtility.RecordSelection(selection, "Split UV Seams");

			foreach (ProBuilderMesh pb in selection)
			{
				pb.ToMesh();

				pb.SplitUVs(pb.selectedIndexesInternal);
				RefreshElementGroups(pb);

				pb.Refresh();
				pb.Optimize();
			}

			SetSelectedUVsWithSceneView();
			RefreshSelectedUVCoordinates();

			EditorUtility.ShowNotification(this, "Split UVs");
		}

		/**
		 * Flips UVs across the provided direction. The current pivot position is used as origin.  Can be horizontal, vertical, or anything in between.
		 */
		public void Menu_FlipUVs(Vector2 direction)
		{
			UndoUtility.RecordSelection(selection, "Flip " + direction);

			Vector2 center = handlePosition;

			for (int i = 0; i < selection.Length; i++)
			{
				selection[i].ToMesh();

				selection[i].SplitUVs(selection[i].selectedIndexesInternal);

				Vector2[] uv = channel == 0 ? selection[i].texturesInternal : selection[i].mesh.uv2;

				foreach (int n in selection[i].selectedIndexesInternal.Distinct())
					uv[n] = Math.ReflectPoint(uv[n], center, center + direction);

				UVEditing.ApplyUVs(selection[i], uv, channel);

				RefreshElementGroups(selection[i]);

				selection[i].Refresh();
				selection[i].Optimize();
			}

			SetSelectedUVsWithSceneView();
			RefreshSelectedUVCoordinates();

			if (direction == Vector2.right)
			{
				EditorUtility.ShowNotification(this, "Flip UVs Vertically");
			}
			else if (direction == Vector2.up)
			{
				EditorUtility.ShowNotification(this, "Flip UVs Horizontally");
			}
			else
			{
				EditorUtility.ShowNotification(this, "Flip UVs");
			}
		}

		/// <summary>
		/// Fit selected UVs to 0,1 space.
		/// </summary>
		public void Menu_FitUVs()
		{
			UndoUtility.RecordSelection(selection, "Fit UVs");

			for (var i = 0; i < selection.Length; i++)
			{
				if (selection[i].selectedVertexCount < 3)
					continue;

				selection[i].ToMesh();

				Vector2[] uv = UVEditing.GetUVs(selection[i], channel);
				Vector2[] uvs = UnityEngine.ProBuilder.ArrayUtility.ValuesWithIndexes(uv, m_DistinctIndexesSelection[i]);

				uvs = UVEditing.FitUVs(uvs);

				for (int n = 0; n < uvs.Length; n++)
					uv[m_DistinctIndexesSelection[i][n]] = uvs[n];

				UVEditing.ApplyUVs(selection[i], uv, channel);
			}

			RefreshSelectedUVCoordinates();
			EditorUtility.ShowNotification(this, "Fit UVs");
		}

		/// <summary>
		/// Moves the selected UVs to where their bounds center is now point, where point is in UV space. Does not call ToMesh or Refresh.
		/// </summary>
		/// <param name="point"></param>
		void CenterUVsAtPoint(Vector2 point)
		{
			Vector2 uv_cen = UVSelectionBounds().center;
			Vector2 delta = uv_cen - point;

			for (int i = 0; i < selection.Length; i++)
			{
				var pb = selection[i];
				Vector2[] uv = UVEditing.GetUVs(pb, channel);

				foreach (int n in selection[i].selectedIndexesInternal.Distinct())
				{
					uv[n] -= delta;
				}

				UVEditing.ApplyUVs(pb, uv, channel);
			}
		}
#endregion
#region Screenshot Rendering

		float curUvScale = 0f;
		///< Store the user set positioning and scale before modifying them for a screenshot
		Vector2 curUvPosition = Vector2.zero;
		///< ditto ^
		Texture2D screenshot;
		Rect screenshotCanvasRect = new Rect(0, 0, 0, 0);
		Vector2 screenshotTexturePosition = Vector2.zero;

		// settings
		int screenshot_size = 1024;
		bool screenshot_hideGrid = true;
		bool screenshot_transparentBackground;
		Color screenshot_lineColor = Color.green;
		Color screenshot_backgroundColor = Color.black;
		string screenShotPath = "";

		readonly Color UV_FILL_COLOR = new Color(.192f, .192f, .192f, 1f);

		///< This is the default background of the UV editor - used to compare bacground pixels when rendering UV template
		void InitiateScreenshot(int ImageSize, bool HideGrid, Color LineColor, bool TransparentBackground, Color BackgroundColor)
		{
			screenshot_size = ImageSize;
			screenshot_hideGrid = HideGrid;
			screenshot_lineColor = LineColor;
			screenshot_transparentBackground = TransparentBackground;
			screenshot_backgroundColor = TransparentBackground ? UV_FILL_COLOR : BackgroundColor;

			// if line color and background color are the same but we want transparent backgruond,
			// make sure that the background fill will be distinguishable from the lines during the
			// opacity wipe
			if (TransparentBackground && (screenshot_lineColor.ApproxC(screenshot_backgroundColor, .001f)))
			{
				screenshot_backgroundColor.r += screenshot_backgroundColor.r < .9f ? .1f : -.1f;
				screenshot_backgroundColor.g += screenshot_backgroundColor.g < .9f ? .1f : -.1f;
				screenshot_backgroundColor.b += screenshot_backgroundColor.b < .9f ? .1f : -.1f;
			}

			screenShotPath = UnityEditor.EditorUtility.SaveFilePanel("Save UV Template", Application.dataPath, "", "png");

			if (string.IsNullOrEmpty(screenShotPath))
				return;

			screenshotStatus = ScreenshotStatus.Done;
			DoScreenshot();
		}

		// Unity 5 changes the starting y position of a window now account for the tab
		float editorWindowTabOffset
		{
			get
			{
				if (IsUtilityWindow<UVEditor>())
					return 0;
				return 11;
			}
		}

		void DoScreenshot()
		{
			switch (screenshotStatus)
			{
				// A new screenshot has been initiated
				case ScreenshotStatus.Done:
					curUvScale = uvGraphScale;
					curUvPosition = uvGraphOffset;

					uvGraphScale = screenshot_size / 256;

#if RETINA_ENABLED
					uvGraphScale /= EditorGUIUtility.pixelsPerPoint;
#endif

					// always begin texture grabs at bottom left
					uvGraphOffset = new Vector2(-ScreenRect.width / 2f, ScreenRect.height / 2f - editorWindowTabOffset);

					screenshot = new Texture2D(screenshot_size, screenshot_size);
					screenshot.hideFlags = (HideFlags)(1 | 2 | 4);
					screenshotStatus = ScreenshotStatus.PrepareCanvas;

					// set the current rect pixel bounds to the largest possible size.  if some parts are out of focus, they'll be grabbed in subsequent passes
					if ((bool)ReflectionUtility.GetValue(this, this.GetType(), "docked"))
						screenshotCanvasRect = new Rect(4, 2, (int)Mathf.Min(screenshot_size, ScreenRect.width - 4), (int)Mathf.Min(screenshot_size, ScreenRect.height - 2));
					else
						screenshotCanvasRect = new Rect(0, 0, (int)Mathf.Min(screenshot_size, ScreenRect.width), (int)Mathf.Min(screenshot_size, ScreenRect.height));

					screenshotTexturePosition = new Vector2(0, 0);

					this.ShowNotification(new GUIContent("Rendering UV Graph\n..."));

					Repaint();

					return;

				case ScreenshotStatus.CanvasReady:

					// take screenshots vertically, then move right, repeat if necessary
					if (screenshotTexturePosition.y < screenshot_size)
					{
						screenshot.ReadPixels(screenshotCanvasRect, (int)screenshotTexturePosition.x, (int)screenshotTexturePosition.y);

#if PB_DEBUG
					Texture2D wholeScreenTexture = new Texture2D((int) ScreenRect.width, (int) ScreenRect.height);
					Rect wholeScreenRect;

					if( (bool) pb_Reflection.GetValue(this, this.GetType(), "docked") )
						wholeScreenRect = new Rect(4, 2, (int) ScreenRect.width - 4, (int) ScreenRect.height - 2 );
					else
						wholeScreenRect = new Rect(0, 0, (int) ScreenRect.width, (int) ScreenRect.height);

					wholeScreenTexture.ReadPixels(wholeScreenRect,
						(int) (EditorGUIUtility.pixelsPerPoint / wholeScreenRect.width),
						(int) (EditorGUIUtility.pixelsPerPoint / wholeScreenRect.height));

					m_DebugUVRenderScreens.Add(wholeScreenTexture);
					#endif

						screenshotTexturePosition.y += screenshotCanvasRect.height;

						if (screenshotTexturePosition.y < screenshot_size)
						{
							// reposition canvas
#if RETINA_ENABLED
							uvGraphOffset.y += screenshotCanvasRect.height / EditorGUIUtility.pixelsPerPoint;
#else
						uvGraphOffset.y += screenshotCanvasRect.height;
						#endif
							screenshotCanvasRect.height = (int)Mathf.Min(screenshot_size - screenshotTexturePosition.y, ScreenRect.height - 12);
							screenshotStatus = ScreenshotStatus.PrepareCanvas;
							Repaint();
							return;
						}
						else
						{
							screenshotTexturePosition.x += screenshotCanvasRect.width;

							if (screenshotTexturePosition.x < screenshot_size)
							{
								// Move right, reset Y
#if RETINA_ENABLED
								uvGraphOffset.x -= screenshotCanvasRect.width / EditorGUIUtility.pixelsPerPoint;
								uvGraphOffset.y = (ScreenRect.height / 2f - editorWindowTabOffset);
#else
							uvGraphOffset.x -= screenshotCanvasRect.width;
							uvGraphOffset.y = ScreenRect.height / 2f - editorWindowTabOffset;
							#endif
								screenshotCanvasRect.width = (int)Mathf.Min(screenshot_size - screenshotTexturePosition.x, ScreenRect.width);
								screenshotTexturePosition.y = 0;
								screenshotCanvasRect.height = (int)Mathf.Min(screenshot_size - screenshotTexturePosition.y, ScreenRect.height - 12);
								screenshotStatus = ScreenshotStatus.PrepareCanvas;
								Repaint();
								return;
							}
						}
					}

					// reset the canvas to it's original position and scale
					uvGraphScale = curUvScale;
					uvGraphOffset = curUvPosition;

					this.RemoveNotification();
					screenshotStatus = ScreenshotStatus.RenderComplete;
					Repaint();
					break;

				case ScreenshotStatus.RenderComplete:

					if (screenshot_transparentBackground)
					{
						Color[] px = screenshot.GetPixels(0);

						for (int i = 0; i < px.Length; i++)

							if (Mathf.Abs(px[i].r - UV_FILL_COLOR.r) < .01f &&
								Mathf.Abs(px[i].g - UV_FILL_COLOR.g) < .01f &&
								Mathf.Abs(px[i].b - UV_FILL_COLOR.b) < .01f)
								px[i] = Color.clear;

						screenshot.SetPixels(px);
						screenshot.Apply();
					}

					this.minSize = Vector2.zero;
					this.maxSize = Vector2.one * 100000f;
					EditorApplication.delayCall += SaveUVRender; // don't run the save image stuff in the UI loop
					screenshotStatus = ScreenshotStatus.Done;
					break;
			}
		}

		void SaveUVRender()
		{
			if (screenshot && !string.IsNullOrEmpty(screenShotPath))
			{
				FileUtility.SaveTexture(screenshot, screenShotPath);
				DestroyImmediate(screenshot);

#if PB_DEBUG
			for(int n = 0; n < m_DebugUVRenderScreens.Count; n++)
			{
				pb_EditorUtility.SaveTexture(m_DebugUVRenderScreens[n], "Assets/uv-render-" + n + ".png");
				DestroyImmediate(m_DebugUVRenderScreens[n]);
			}
			m_DebugUVRenderScreens.Clear();
			#endif
			}
		}
#endregion
	}
}
