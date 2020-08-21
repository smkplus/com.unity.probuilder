﻿using System;

namespace UnityEngine.ProBuilder
{
    [RequireComponent(typeof(ProBuilderMesh))]
    public class ShapeComponent : MonoBehaviour
    {
        [SerializeReference]
        Shape m_Shape = new Cube();

        public Shape shape {
            get { return m_Shape; }
            set { m_Shape = value; }
        }

        ProBuilderMesh m_Mesh;

        [SerializeField]
        Vector3 m_Size;

        [SerializeField]
        Vector3 m_Rotation;

        [SerializeField]
        Quaternion m_RotationQuaternion = Quaternion.identity;

        public Quaternion rotationQuaternion {
            get {
                return m_RotationQuaternion;
            }
            set {
                m_RotationQuaternion = value;
                m_Rotation = m_RotationQuaternion.eulerAngles;
            }
        }

        public Vector3 size {
            get { return m_Size; }
            set { m_Size = value; }
        } 

        public ProBuilderMesh mesh {
            get { return m_Mesh == null ? m_Mesh = GetComponent<ProBuilderMesh>() : m_Mesh; }
        }

        // Bounds where center is in world space, size is mesh.bounds.size
        internal Bounds meshFilterBounds {
            get {
                var mb = mesh.mesh.bounds;
                return new Bounds(transform.TransformPoint(mb.center), mb.size);
            }
        }

        public void Rebuild(Bounds bounds, Quaternion rotation)
        {
            size = Math.Abs(bounds.size);
            transform.position = bounds.center;
            transform.rotation = rotation;
            Rebuild();
        }

        public void Rebuild()
        {
            m_Shape.RebuildMesh(mesh, size);
            ApplyRotation(rotationQuaternion);
            MeshUtility.FitToSize(mesh, size);
        }

        public void SetShape(Shape shape)
        {
            this.m_Shape = shape;
            Rebuild();
        }

        /// <summary>
        /// Set the rotation of the Shape to a given quaternion, then rotates it while respecting the bounds
        /// </summary>
        /// <param name="angles">The angles to rotate by</param>
        public void SetRotation(Quaternion angles)
        {
            rotationQuaternion = angles;
            ApplyRotation(rotationQuaternion);
            MeshUtility.FitToSize(mesh, size);
        }

        /// <summary>
        /// Rotates the Shape by a given quaternion while respecting the bounds
        /// </summary>
        /// <param name="rotation">The angles to rotate by</param>
        public void Rotate(Quaternion rotation)
        {
            if (rotation == Quaternion.identity)
            {
                return;
            }
            rotationQuaternion = rotation * rotationQuaternion;
            ApplyRotation(rotationQuaternion);
            MeshUtility.FitToSize(mesh, size);
        }

        void ApplyRotation(Quaternion rotation)
        {
            if (rotation == Quaternion.identity)
            {
                return;
            }
            m_Shape.RebuildMesh(mesh, size);

            var origVerts = mesh.positionsInternal;

            for (int i = 0; i < origVerts.Length; ++i)
            {
                origVerts[i] = rotation * origVerts[i];
            }
            mesh.mesh.vertices = origVerts;
            mesh.ReplaceVertices(origVerts);
        }
    }
}
