using UnityEngine;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using ProBuilder2.Common;

#if PB_DEBUG
using Parabox.Debug;
#endif

/**
 *	Used to query pb_Objects for more detailed information than what would belong in the pbObejct class
 */
namespace ProBuilder2.MeshOperations
{
	public class pbMeshUtils
	{
#region Get Connected Elements

	#region FACE
		/**
		 * Returns all faces that share an edge with originFace.  If calling multiple times, use the variation that 
		 * accepts a dictionary lookup to  save to the cost of generating it each call.
		 */
		public static List<pb_Face> GetNeighborFaces(pb_Object pb, pb_Face originFace, Dictionary<int, int> lookup = null, IEnumerable<pb_Face> mask = null)
		{
			if(lookup == null)
				lookup = pb.sharedIndices.ToDictionary();

			List<pb_Face> faces = new List<pb_Face>();

			HashSet<pb_Edge> sharedEdges = new HashSet<pb_Edge>();

			for(int i = 0; i < originFace.edges.Length; i++)
			{
				sharedEdges.Add(new pb_Edge(lookup[originFace.edges[i].x], lookup[originFace.edges[i].y]));
			}

			pb_Edge edge_s = new pb_Edge(-1,-1);

			for(int i = 0; i < pb.faces.Length; i++)
			{		
				foreach(pb_Edge edge in pb.faces[i].edges)
				{
					edge_s.x = lookup[edge.x];
					edge_s.y = lookup[edge.y];

					bool contains = sharedEdges.Contains(edge_s);

					if( contains && (mask == null || !mask.Contains(pb.faces[i])) )
					{

						faces.Add(pb.faces[i]);
						break;
					}
				}
			}

			return faces;
		}

		/**
		 * Generates a Dictionary where each face is a key, and its value is a list of all faces adjacent.
		 */
		public static Dictionary<pb_Face, List<pb_Face>> GenerateNeighborLookup(pb_Object pb, IList<pb_Face> InFaces)
		{
			Dictionary<int, int> sharedLookup = pb.sharedIndices.ToDictionary();
			Dictionary<pb_Face, List<pb_Face>> faceLookup = new Dictionary<pb_Face, List<pb_Face>>();

			IList<pb_Face> faces = InFaces;
			int faceCount = faces.Count();
			List<pb_Face> list;

			HashSet<pb_Edge>[] universal = new HashSet<pb_Edge>[faceCount];

			for(int i = 0; i < faceCount; i++)
				universal[i] = new HashSet<pb_Edge>(pb_Edge.GetUniversalEdges(faces[i].edges, sharedLookup));

			for(int i = 0; i < faceCount-1; i++)
			{
				if( !faceLookup.ContainsKey(faces[i]) )
					faceLookup.Add(faces[i], new List<pb_Face>());

				for(int n = i+1; n < faceCount; n++)
				{
					bool overlaps = universal[i].Overlaps(universal[n]);
					
					if( overlaps )
					{
						faceLookup[faces[i]].Add(faces[n]);

						if( faceLookup.TryGetValue(faces[n], out list) )
							list.Add(faces[i]);
						else
							faceLookup.Add(faces[n], new List<pb_Face>() {faces[i]});
					}
				}
			}

			return faceLookup;
		}

		/**
		 * \brief Returns faces that share an edge with any of @c selFcaes.
		 */
		public static pb_Face[] GetNeighborFaces(pb_Object pb, Dictionary<int, int> sharedIndicesLookup, pb_Face[] selFaces)
		{
			List<pb_Face> perimeterFaces = new List<pb_Face>();

			pb_Edge[] perimeterEdges = pbMeshUtils.GetPerimeterEdges(pb, sharedIndicesLookup, selFaces).ToArray();
			pb_Edge[] universalEdges = new pb_Edge[perimeterEdges.Length];

			for(int i = 0; i < perimeterEdges.Length; i++)
				universalEdges[i] = new pb_Edge( sharedIndicesLookup[perimeterEdges[i].x],
												 sharedIndicesLookup[perimeterEdges[i].y]);

			pb_Edge edge_u = new pb_Edge(-1, -1);

			HashSet<pb_Face> skip = new HashSet<pb_Face>(selFaces);

			foreach(pb_Face face in pb.faces)
			{
				if(skip.Contains(face))
				{
					skip.Remove(face);
					continue;
				}

				foreach(pb_Edge edge in face.edges)
				{
					edge_u.x = sharedIndicesLookup[edge.x];
					edge_u.y = sharedIndicesLookup[edge.y];

					if(universalEdges.Contains(edge_u))
					{
						perimeterFaces.Add(face);
						break;
					}
				}
			}

			return perimeterFaces.ToArray();
		}
	#endregion

	#region EDGE

		/**
		 *	Returns a list of pb_Tuple<pb_Face, pb_Edge> where each face is connected to the passed edge.
		 */
		public static List<pb_Tuple<pb_Face, pb_Edge>> GetNeighborFaces(pb_Object pb, pb_Edge edge, Dictionary<int, int> lookup = null)
		{
			if(lookup == null)
				lookup = pb.sharedIndices.ToDictionary();

			List<pb_Tuple<pb_Face, pb_Edge>> faces = new List<pb_Tuple<pb_Face, pb_Edge>>();

			pb_Edge uni = new pb_Edge(lookup[edge.x], lookup[edge.y]);
			pb_Edge e = new pb_Edge(0,0);

			for(int i = 0; i < pb.faces.Length; i++)
			{
				pb_Edge[] edges = pb.faces[i].edges;
				for(int n = 0; n < edges.Length; n++)
				{
					e.x = edges[n].x;
					e.y = edges[n].y;

					if( (uni.x == lookup[e.x] && uni.y == lookup[e.y]) || 
						(uni.x == lookup[e.y] && uni.y == lookup[e.x]))
					{
						faces.Add(new pb_Tuple<pb_Face, pb_Edge>(pb.faces[i], new pb_Edge(edges[n])));
						break;
					}
				}
			}
			return faces;
		}

		// todo update this and ^ this with faster variation below
		public static pb_Face[] GetNeighborFaces(pb_Object pb, pb_Edge[] edges)
		{
			List<pb_Face> faces = new List<pb_Face>();
			Dictionary<int, int> sharedIndices = pb.sharedIndices.ToDictionary();
			foreach(pb_Face f in pb.faces)
			{
				foreach(pb_Edge e in edges)
					if(f.edges.IndexOf(e, sharedIndices) > -1)
						faces.Add(f);
			}

			return faces.Distinct().ToArray();
		}


		internal static List<pb_Face>[][] GetNeighborFacesJagged(pb_Object pb, pb_Edge[][] selEdges)
		{
			int len = selEdges.Length;

			List<pb_Face>[][] faces = new List<pb_Face>[len][];
			for(int j = 0; j < len; j++)
			{
				faces[j] = new List<pb_Face>[selEdges[j].Length];
				for(int i = 0; i < selEdges[j].Length; i++)
					faces[j][i] = new List<pb_Face>();
			}

			pb_IntArray[] sharedIndices = pb.sharedIndices;
				
			pb_Edge[][] sharedEdges = new pb_Edge[len][];
			for(int i = 0; i < len; i++)
				sharedEdges[i] = pb_Edge.GetUniversalEdges(selEdges[i], sharedIndices).Distinct().ToArray();

			for(int i = 0; i < pb.faces.Length; i++)
			{
				pb_Edge[] faceEdges = pb_Edge.GetUniversalEdges(pb.faces[i].edges, sharedIndices).Distinct().ToArray();
				
				for(int j = 0; j < len; j++)
				{
					int ind = -1;
					for(int t = 0; t < sharedEdges[j].Length; t++)
					{
						if(faceEdges.Contains(sharedEdges[j][t]))
						{
							ind = t;
							break;
						}
					}

					if(ind > -1)
						faces[j][ind].Add(pb.faces[i]);
				}
			}

			return faces;
		}
	#endregion

	#region VERTICES

		/**
		 *	Returns all faces connected to the passed vertex index.
		 */
		public static List<pb_Face> GetNeighborFaces(pb_Object pb, int index)
		{
			List<pb_Face> faces = new List<pb_Face>();
			pb_IntArray[] sharedIndices = pb.sharedIndices;
			int i = sharedIndices.IndexOf(index);

			foreach(pb_Face f in pb.faces)
			{
				if(f.distinctIndices.ContainsMatch((int[])sharedIndices[i]))
					faces.Add(f);
			}
			return faces;
		}

		/**
		 * Gets all faces connected to each index taking into account shared vertices.
		 */
		public static IEnumerable<pb_Face> GetNeighborFaces(pb_Object pb, IEnumerable<int> indices)
		{
			List<pb_Face> neighboring = new List<pb_Face>();
			Dictionary<int, int> lookup = pb.sharedIndices.ToDictionary();
			
			HashSet<int> shared = new HashSet<int>();

			foreach(int tri in indices)
				shared.Add(lookup[tri]);

			for(int i = 0; i < pb.faces.Length; i++)
			{
				int[] dist = pb.faces[i].distinctIndices;

				for(int n = 0; n < dist.Length; n++)
				{
					if( shared.Contains(lookup[dist[n]]))
					{
						neighboring.Add(pb.faces[i]);
						break;
					}
				}
			}

			return neighboring;
		}

		/**
		 * Returns a unique array of Edges connected to the passed vertex indices.
		 */
		public static pb_Edge[] GetConnectedEdges(pb_Object pb, int[] indices)
		{
			Dictionary<int, int> lookup = pb.sharedIndices.ToDictionary();

			List<pb_Edge> connectedEdges = new List<pb_Edge>();

			HashSet<int> shared = new HashSet<int>();
			for(int i = 0; i < indices.Length; i++)	
				shared.Add(lookup[indices[i]]);

			pb_Edge[] edges = pb_Edge.AllEdges(pb.faces);
			HashSet<pb_Edge> used = new HashSet<pb_Edge>();

			pb_Edge uni = new pb_Edge(0,0);

			for(int i = 0; i < edges.Length; i++)
			{
				pb_Edge key = new pb_Edge(lookup[edges[i].x], lookup[edges[i].y]);

				if( shared.Contains(key.x) || shared.Contains(key.y) && !used.Contains(uni) )
				{
					connectedEdges.Add(edges[i]);
					used.Add(key);
				}
			}

			return connectedEdges.ToArray();
		}
	#endregion
#endregion

#region Perimeter

		/**
		 * Get all edges that are on the perimeter of this face group selection.
		 */
		public static IEnumerable<pb_Edge> GetPerimeterEdges(pb_Object pb, IEnumerable<pb_Face> faces)
		{
			return GetPerimeterEdges(pb, pb.sharedIndices.ToDictionary(), faces);
		}

		/**
		 * Get all edges that are on the perimeter of this face group selection.
		 */
		public static IEnumerable<pb_Edge> GetPerimeterEdges(pb_Object pb, Dictionary<int, int> sharedIndicesLookup, IEnumerable<pb_Face> faces)
		{
			List<pb_Edge> faceEdges = faces.SelectMany(x => x.edges).ToList();	// actual edges
			int edgeCount = faceEdges.Count;

			// translate all face edges to universal edges
			Dictionary<pb_Edge, List<pb_Edge>> dup = new Dictionary<pb_Edge, List<pb_Edge>>();
			List<pb_Edge> list;

			for(int i = 0; i < edgeCount; i++)
			{
				pb_Edge uni = new pb_Edge( sharedIndicesLookup[faceEdges[i].x], sharedIndicesLookup[faceEdges[i].y] );

				if( dup.TryGetValue(uni, out list) )
					list.Add(faceEdges[i]);
				else
					dup.Add(uni, new List<pb_Edge>() { faceEdges[i] });
			}

			return dup.Where(x => x.Value.Count < 2).Select(x => x.Value[0]);
		}

		/**
		 * Returns the indices of perimeter edges in a given element group.
		 * todo - to speed this up, we could just use the distinct in GetUniversalEdges() - but that would 
		 * break this method's usefullness in other situations.
		 */
		public static int[] GetPerimeterEdges(pb_Object pb, pb_Edge[] edges)
		{
			if(edges.Length == pb_Edge.AllEdges(pb.faces).Length || edges.Length < 3)
				return new int[] {};

			// Figure out how many connections each edge has to other edges in the selection
			pb_Edge[] universal = pb_Edge.GetUniversalEdges(edges, pb.sharedIndices.ToDictionary());
			int[] connections = new int[universal.Length];

			for(int i = 0; i < universal.Length - 1; i++)
			{
				for(int n = i+1; n < universal.Length; n++)
				{
					if( universal[i].x == universal[n].x || universal[i].x == universal[n].y ||
						universal[i].y == universal[n].x || universal[i].y == universal[n].y )
					{
						connections[i]++;
						connections[n]++;
					}
				}
			}

			int min = pb_Math.Min(connections);
			List<int> perimeter = new List<int>();

			for(int i = 0; i < connections.Length; i++)	
			{
				if(connections[i] <= min)	
					perimeter.Add(i);
			}

			return perimeter.Count != edges.Length ? perimeter.ToArray() : new int[] {};
		}

		/**
		 * Returns an array of faces where each face has at least one non-shared edge.
		 */
		public static IEnumerable<pb_Face> GetPerimeterFaces(pb_Object pb, IEnumerable<pb_Face> faces)
		{
			Dictionary<int, int> lookup = pb.sharedIndices.ToDictionary();
			Dictionary<pb_Edge, List<pb_Face>> sharedEdges = new Dictionary<pb_Edge, List<pb_Face>>();

			/**
			 * To be considered a perimeter face, at least one edge must not share
			 * any boundary with another face.
			 */

			foreach(pb_Face face in faces)
			{
				foreach(pb_Edge e in face.edges)
				{
					pb_Edge edge = new pb_Edge( lookup[e.x], lookup[e.y]);

					if( sharedEdges.ContainsKey(edge) )
						sharedEdges[edge].Add(face);
					else
						sharedEdges.Add(edge, new List<pb_Face>() { face } );
				}
			}

			return sharedEdges.Where(x => x.Value.Count < 2).Select(x => x.Value[0]).Distinct();
		}

		/**
		 * Returns the indices of perimeter vertices in selection.
		 */
		public static int[] GetPerimeterVertices(pb_Object pb, int[] indices, pb_Edge[] universal_edges_all)
		{
			int len = indices.Length;
			pb_IntArray[] sharedIndices = pb.sharedIndices;
			int[] universal = new int[len];

			for(int i = 0; i < len; i++)
				universal[i] = sharedIndices.IndexOf(indices[i]);

			int[] connections = new int[indices.Length];
			for(int i = 0; i < indices.Length - 1; i++)
			{
				for(int n = i+1; n < indices.Length; n++)
				{
					if(universal_edges_all.Contains(universal[i], universal[n]))
					{
						connections[i]++;
						connections[n]++;
					}
				}
			}

			int min = pb_Math.Min(connections);
			List<int> perimeter = new List<int>();
			for(int i = 0; i < len; i++)
			{
				if(connections[i] <= min)
					perimeter.Add(i);
			}

			return perimeter.Count < len ? perimeter.ToArray() : new int[] {};
		}
#endregion

#region Edge Ring / Loop
	
		/**
		 * Iterates through face edges and builds a list using the opposite edge.
		 * @todo Lots of slow stuff in here
		 */
		public static pb_Edge[] GetEdgeRing(pb_Object pb, pb_Edge[] edges)
		{
			List<pb_Edge> usedEdges = new List<pb_Edge>();
			Dictionary<int, int> lookup = pb.sharedIndices.ToDictionary();

			foreach(pb_Edge e in edges)
			{	
				List<pb_Face> origFace;
				List<pb_Edge> origEdge;

				// ValidFaceAndEdgeWithEdge will return false if < 1 face and edge combo is found.
				if( !ValidFaceAndEdgeWithEdge(pb, e, lookup, out origFace, out origEdge) )
					continue;
					
				// Only add the initial edge once
				usedEdges.Add(origEdge[0]);

				pb_Face opFace;
				pb_Edge opEdge;

				bool superBreak = false;
				for(int i = 0; i < origFace.Count; i++)
				{
					pb_Face curFace = origFace[i];
					pb_Edge curEdge = origEdge[i];

					while( GetOppositeEdge(pb, curFace, curEdge, lookup, out opFace, out opEdge) )
					{
						curFace = opFace;
						curEdge = opEdge;

						usedEdges.Add(curEdge);
						
						if(curFace == null)
							break;	

						if(curFace == origFace[i])
						{
							superBreak = true;
							break;
						}
					}

					if(superBreak)
						break;
				}
			}

			pb_Edge[] dist = pb_Edge.GetUniversalEdges(usedEdges.ToArray(), lookup);


			return pb_Edge.GetLocalEdges_Fast(dist.Distinct().ToArray(), pb.sharedIndices);
		}

		/**
		 * Attempts to find edges along an Edge loop.
		 * 
		 * http://wiki.blender.org/index.php/Doc:2.4/Manual/Modeling/Meshes/Selecting/Edges says:
		 * 	First check to see if the selected element connects to only 3 other edges.
		 * 	If the edge in question has already been added to the list, the selection ends.
		 * 	Of the 3 edges that connect to the current edge, the ones that share a face with the current edge are eliminated and the remaining edge is added to the list and is made the current edge.
		 */	
		public static bool GetEdgeLoop(pb_Object pb, pb_Edge[] edges, out pb_Edge[] loop)
		{
			List<pb_Edge> valid_edges = pb_Edge.ValidateEdges(pb, edges);

			List<pb_WingedEdge> wings = pb_WingedEdge.GenerateWingedEdges(pb);
			Dictionary<pb_Edge, pb_WingedEdge> wings_dic = new Dictionary<pb_Edge, pb_WingedEdge>();
			for(int i = 0; i < wings.Count; i++)
				wings_dic.Add(wings[i].edge.local, wings[i]);

			HashSet<pb_EdgeLookup> used = new HashSet<pb_EdgeLookup>();

			for(int i = 0; i < edges.Length; i++)
			{		
				pb_WingedEdge we = null;

				if(!wings_dic.TryGetValue(valid_edges[i], out we) || used.Contains(we.edge))
					continue;

				pb_WingedEdge cur = we;

				int superBreak = 0;
				const int LOOP_BREAKER_MAX = 65000;

				while(cur != null && superBreak++ < LOOP_BREAKER_MAX)
				{
					if(!used.Add(cur.edge)) break;
					cur = EdgeLoopNext(cur, true);
				}

				superBreak = 0;
				cur = EdgeLoopNext(we, false);

				// have to test in both directions
				while(cur != null && superBreak++ < LOOP_BREAKER_MAX)
				{
					if(!used.Add(cur.edge)) break;
					cur = EdgeLoopNext(cur, false);
				}
			}

			loop = used.Select(x => x.local).ToArray();
			return true;
		}

		private static pb_WingedEdge EdgeLoopNext(pb_WingedEdge edge, bool forward)
		{
			if(edge == null)
				return null;

			pb_WingedEdge next = forward ? edge.next : edge.previous;

			if(next == null)
				return null;

			pb_WingedEdge opposite = next.opposite;

			if(opposite == null)
				return null;

			pb_WingedEdge opposite_next = opposite.next;

			if(opposite_next != null && opposite_next.edge.common.Contains(edge.edge.common))
				return opposite_next;

			pb_WingedEdge opposite_previous = opposite.previous;

			if(opposite_previous != null && opposite_previous.edge.common.Contains(edge.edge.common))
				return opposite_previous;

			return null;
		}
#endregion

#region Utility

		/**
		 * The SelectedEdges array contains Edges made up of indices that aren't guaranteed to be 'valid' - that is, they
		 * may not belong to the same face.  This method extracts an edge and face combo from the face independent edge
		 * selection.
		 * @param faces - Corresponding face to edge list
		 * @param edges - An edge composed of indices that belong to a same face (matching face in faces List).
		 * @returns True if at least one valid edge is found, false if not.
		 */
		public static bool ValidFaceAndEdgeWithEdge(pb_Object pb, pb_Edge faceIndependentEdge, Dictionary<int, int> sharedIndices, out List<pb_Face> faces, out List<pb_Edge> edges)
		{
			faces = new List<pb_Face>();
			edges = new List<pb_Edge>();

			foreach(pb_Face f in pb.faces)
			{
				int ind = f.edges.IndexOf(faceIndependentEdge, sharedIndices);
				
				if(ind > -1)
				{
					faces.Add(f);
					edges.Add(f.edges[ind]);
				}
			}

			return faces.Count > 0;
		}

		/**
		 * Returns the opposite edge on the neighboring face (if possible - if the edge does not connect to an additional face opposite_face will be null).
		 */
		public static bool GetOppositeEdge(pb_Object pb, pb_Face face, pb_Edge edge, Dictionary<int, int> lookup, out pb_Face opposite_face, out pb_Edge opposite_edge)
		{
			opposite_face = null;
			opposite_edge = null;
			
			if(face.edges.Length != 4) return false;
 
			// Construct a list of all edges starting at vertex edge.y and going around the face.  Then grab the middle edge.
			pb_Edge[] ordered_edges = new pb_Edge[face.edges.Length];
			ordered_edges[0] = edge;

			for(int i = 1; i < face.edges.Length; i++)
			{
				foreach(pb_Edge e in face.edges)
				{
					if(e.x == ordered_edges[i-1].y)
					{
						ordered_edges[i] = e;
						break;
					}
				}
			}
			pb_Edge opEdgeLocal = ordered_edges[face.edges.Length/2];

			List<pb_Tuple<pb_Face, pb_Edge>> connectedFaces = pbMeshUtils.GetNeighborFaces(pb, opEdgeLocal, lookup);
			connectedFaces.RemoveAll(x => x.Item1 == face);

			if(connectedFaces.Count < 1)
			{
				opposite_edge = opEdgeLocal;	// sometimes ya still want this edge (planes, for example)
				return true;
			}

			opposite_face = connectedFaces[0].Item1;
			
			for(int i = 0; i < opposite_face.edges.Length; i++)
			{
				if(opposite_face.edges[i].Equals(opEdgeLocal, lookup))
				{
					opposite_edge = opposite_face.edges[i];
					break;
				}
			}

			return true;
		}
	}
#endregion
}
