﻿using UnityEngine;
using ProBuilder2.Common;
using System.Collections;

namespace ProBuilder2.EditorCommon
{
	/**
	 * Helper functions that are only available in the Editor.
	 */
	public static class pb_Editor_Mesh_Utility
	{
		/**
		 * Optmizes the mesh geometry, and generates a UV2 channel (if automatic lightmap generation is enabled).
		 */
		public static void Optimize(this pb_Object InObject)
		{
			pb_MeshUtility.CollapseSharedVertices(InObject);	///< Merge compatible shared vertices to a single vertex.
			InObject.GenerateUV2();
		}
	}
}