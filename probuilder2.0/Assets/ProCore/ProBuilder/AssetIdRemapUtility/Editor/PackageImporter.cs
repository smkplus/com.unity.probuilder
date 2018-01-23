﻿using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ProBuilder.AssetUtility
{
	class PackageImporter : AssetPostprocessor
	{
		static readonly string[] k_AssetStoreInstallGuids = new string[]
		{
			"0472bdc8d6d15384d98f22ee34302f9c", // ProBuilderCore
			"b617d7797480df7499f141d87e13ebc5", // ProBuilderMeshOps
			"4df21bd079886d84699ca7be1316c7a7"  // ProBuilderEditor
		};

#pragma warning disable 414
		static readonly string[] k_PackageManagerInstallGuids = new string[]
		{
			"4f0627da958b4bb78c260446066f065f", // Core
			"9b27d8419276465b80eb88c8799432a1", // Mesh Ops
			"e98d45d69e2c4936a7382af00fd45e58", // Editor
		};
#pragma warning restore 414

		const string k_PackageManagerEditorCore = "e98d45d69e2c4936a7382af00fd45e58";
		const string k_AssetStoreEditorCore = "4df21bd079886d84699ca7be1316c7a7";

		internal static string EditorCorePackageManager { get { return k_PackageManagerEditorCore; } }
		internal static string EditorCoreAssetStore { get { return k_AssetStoreEditorCore; } }

#if DEBUG
		static PackageImporter()
		{
			AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
		}

		static void OnImportPackageCompleted(string name)
		{
			Debug.Log("OnImportPackageCompleted: " + name);
		}
#endif

		static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			CheckEditorCoreEnabled();
		}

		static void CheckEditorCoreEnabled()
		{
			string editorCoreDllPath = AssetDatabase.GUIDToAssetPath(k_PackageManagerEditorCore);
			var importer = AssetImporter.GetAtPath(editorCoreDllPath) as PluginImporter;

			if (importer != null)
			{
				bool assetStoreInstall = IsPreUpmProBuilderInProject();
				bool isEnabled = importer.GetCompatibleWithEditor();

				if (isEnabled == assetStoreInstall)
				{
//					Debug.Log(isEnabled ? "Disabling ProBuilder Package Manager version" : " Enabling ProBuilder Package Manager version");
//					importer.SetCompatibleWithAnyPlatform(false);
//					importer.SetCompatibleWithEditor(!assetStoreInstall);
//					AssetDatabase.ImportAsset(editorCoreDllPath);

					if (isEnabled)
					{
						CancelProBuilderImportPopup();

						if (EditorUtility.DisplayDialog("Conflicting ProBuilder Install in Project",
							"The Asset Store version of ProBuilder is incompatible with Package Manager. Would you like to convert your project to the Package Manager version of ProBuilder?\n\nIf you choose \"No\" the Package Manager ProBuilder package will be disabled.",
							"Yes", "No"))
							EditorApplication.delayCall += AssetIdRemapUtility.OpenConversionEditor;
						else
							Debug.Log("ProBuilder Package Manager conversion process cancelled. You can initiate this conversion at a later time via Tools/ProBuilder/Repair/Convert to Package Manager.");
					}
				}
			}
		}

		internal static void SetEditorDllEnabled(string guid, bool isEnabled)
		{
			string dllPath = AssetDatabase.GUIDToAssetPath(guid);

			var importer = AssetImporter.GetAtPath(dllPath) as PluginImporter;

			if (importer != null)
			{
				importer.SetCompatibleWithAnyPlatform(false);
				importer.SetCompatibleWithEditor(isEnabled);
			}
		}

		internal static bool IsEditorPluginEnabled(string guid)
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			var importer = AssetImporter.GetAtPath(path) as PluginImporter;
			if (!importer)
				return false;
			return importer.GetCompatibleWithEditor() && !importer.GetCompatibleWithAnyPlatform();
		}

		internal static void Reimport(string guid)
		{
			string dllPath = AssetDatabase.GUIDToAssetPath(guid);

			if(!string.IsNullOrEmpty(dllPath))
				AssetDatabase.ImportAsset(dllPath);
		}

		static bool AreAnyAssetsAreLoaded(string[] guids)
		{
			foreach (var id in guids)
			{
				if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(id)) != null)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Check if any pre-3.0 ProBuilder package is present in the project
		/// </summary>
		/// <returns></returns>
		internal static bool IsPreUpmProBuilderInProject()
		{
			// easiest check, are any of the dlls from asset store present
			if (AreAnyAssetsAreLoaded(k_AssetStoreInstallGuids))
				return true;

			// next check if the source version is in the project
			string[] pbObjectMonoScripts = Directory.GetFiles("Assets", "pb_Object.cs", SearchOption.AllDirectories);

			foreach (var pbScriptPath in pbObjectMonoScripts)
			{
				if (pbScriptPath.EndsWith(".cs"))
				{
					MonoScript ms = AssetDatabase.LoadAssetAtPath<MonoScript>(pbScriptPath);

					if (ms != null)
					{
						Type type = ms.GetClass();
						// pre-3.0 didn't have ProBuilder.Core namespace
						return type.ToString().Equals("pb_Object");
					}
				}
			}

			return false;
		}

		static Type FindType(string typeName)
		{
			// First try the current assembly
			Type found = Type.GetType(typeName);

			// Then scan the loaded assemblies
			if (found == null)
			{
				foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					found = assembly.GetType(typeName);

					if (found != null)
						break;
				}
			}

			return found;
		}

		internal static bool IsUpmProBuilderLoaded()
		{
			if (IsEditorPluginEnabled(k_PackageManagerEditorCore))
				return true;

			Type versionUtilType = FindType("ProBuilder.EditorCore.pb_VersionUtil");

			if (versionUtilType == null)
				return false;

			MethodInfo isVersionGreaterThanOrEqualTo = versionUtilType.GetMethod("IsGreaterThanOrEqualTo",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

			if (isVersionGreaterThanOrEqualTo != null)
				return (bool) isVersionGreaterThanOrEqualTo.Invoke(null, new object[] {2, 10, 0});

			return false;
		}

		internal static void CancelProBuilderImportPopup()
		{
			Type aboutWindowType = FindType("ProBuilder.EditorCore.pb_AboutWindow");

			if (aboutWindowType != null)
			{
				MethodInfo cancelPopupMethod =
					aboutWindowType.GetMethod("CancelImportPopup", BindingFlags.Public | BindingFlags.Static);

				if(cancelPopupMethod != null)
					cancelPopupMethod.Invoke(null, null);
			}
		}
	}
}
