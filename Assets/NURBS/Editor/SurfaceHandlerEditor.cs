using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace kmty.NURBS {
    [CustomEditor(typeof(SurfaceHandler))]
    public class SurfaceHandlerEditor : Editor {
        protected int order;
        protected bool xloop;
        protected bool yloop;
        protected List<int> idcs = new List<int>();
        private bool showWireframe => _activeInstances.ContainsKey(target.GetInstanceID());
        private static Dictionary<int, SurfaceHandlerEditor> _activeInstances = new();
        private SurfaceHandlerEditor theActiveInstance;
        private bool deactivateOnDisable = true;
        
        public override void OnInspectorGUI() {
            base.OnInspectorGUI();
            EditorGUILayout.Space(1);
            var h = (SurfaceHandler)target;
            if (GUILayout.Button("Bake Mesh")) {
                var path = $"{h.BakePath}/{h.BakeName}.asset";
                CreateOrUpdate(Weld(h.mesh), path);
            }

            if (theActiveInstance == this)
                // this is enabled
            {
                if (deactivateOnDisable)
                {
                    if (GUILayout.Button("Keep Wireframe in Editor"))
                        deactivateOnDisable = false;
                }
                else
                {
                    if (GUILayout.Button("Hide Wireframe"))
                        deactivateOnDisable = true;
                }
            }
            else
            {
                // another instance is enabled, this is disabled
                if (GUILayout.Button("Hide Wireframe"))
                {
                    theActiveInstance.Disable(); // this is still active: deactivateOnDisable = false
                    this.Enable();
                }
            }
        }

        // When the object is selected, a new instance of this class is created and OnEnable is called.
        void OnEnable()
        {
            if (_activeInstances.TryGetValue(target.GetInstanceID(), out var editor))
                theActiveInstance = editor;
            else
                Enable();
        }

        private void Enable()
        {
            theActiveInstance = this;
            _activeInstances.Add(target.GetInstanceID(), this);
            SceneView.duringSceneGui += OnSceneGUI;
        }

        // When the object is deselected, this is called.
        private void OnDisable()
        {
            if (deactivateOnDisable && theActiveInstance == this /* this is enabled */)
                Disable();
        }

        void Disable()
        {
            _activeInstances.Remove(target.GetInstanceID());
            theActiveInstance = null;
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (theActiveInstance != this) return;
            if (target == null) return;
            var cache = Handles.zTest;

            var h = (SurfaceHandler)target;
            var e = Event.current.type;
            var q = Quaternion.identity;
            var selected = false;
            var data = h.Data;
            if (data == null) return;   
            var cps  = data.cps;
            if (h.segments.Count == 0) h.UpdateSegments(data, h.transform.position);

            if (data.order != order || data.GetXLoop() != xloop || data.GetYLoop() != yloop) {
                if (Application.isPlaying) h.Init();
                order = data.order;
                xloop = data.GetXLoop();
                yloop = data.GetYLoop();
            };

            if (Application.isPlaying) {
                for (int i = 0; i < cps.Count; i++) {
                    var cp = cps[i];
                    h.surf.SetCP(data.Convert(i), new CP(h.transform.position + cp.pos, cp.weight));
                }
            }

            Handles.zTest = CompareFunction.Less;
            Handles.color = Color.cyan;
            Handles.DrawLines(h.segments.ToArray());
            Handles.color = Color.white;

            for(var i = 0; i < cps.Count; i++) {
                var w = h.transform.TransformPoint(cps[i].pos);
                var s = Mathf.Min(HandleUtility.GetHandleSize(w) * 0.1f, 0.1f);
                if (Handles.Button(w, q, s, s, Handles.SphereHandleCap)) {
                    idcs.Add(i);
                    selected = true;
                    Repaint();
                }
            }

            if (e == EventType.MouseUp && !selected) idcs.Clear();
            Handles.zTest = CompareFunction.Always;
            Handles.color = Color.HSVToRGB(30f / 360, 1, 1);

            if (idcs.Count > 0) {
                var sum = Vector3.zero;
                foreach (var i in idcs) {
                    var w = h.transform.TransformPoint(cps[i].pos);
                    var s = Mathf.Min(HandleUtility.GetHandleSize(w) * 0.1f, 0.1f);
                    sum += w;
                    Handles.SphereHandleCap(0, w, q, s, Event.current.type);
                }
                EditorGUI.BeginChangeCheck();
                var d = sum / idcs.Count;
                var p = Handles.DoPositionHandle(d, q);
                if (EditorGUI.EndChangeCheck()) {
                    foreach (var i in idcs) {
                        var c = cps[i];
                        c.pos += h.transform.InverseTransformPoint(p - d);
                        cps[i] = c;
                    }
                    EditorUtility.SetDirty(h.Data);
                }
            }

            if (Application.isPlaying) h.UpdateMesh();
            h.UpdateSegments(data, h.transform.position);
            Handles.zTest = cache;
        }
        // void OnSceneGUI() => OnSceneGUI(null);

        void CreateOrUpdate(Object altAsset, string assetPath) {
            var oldAsset = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (oldAsset == null) {
                AssetDatabase.CreateAsset(altAsset, assetPath);
            } else {
                EditorUtility.CopySerializedIfDifferent(altAsset, oldAsset);
                AssetDatabase.SaveAssets();
            }
        }

        Mesh Weld(Mesh original) {
            var ogl_vrts = original.vertices;
            var ogl_idcs = original.triangles;
            var alt_mesh = new Mesh();
            var alt_vrts = ogl_vrts.Distinct().ToArray();
            var alt_idcs = new int[ogl_idcs.Length];
            var vrt_rplc = new int[ogl_vrts.Length];
            for (var i = 0; i < ogl_vrts.Length; i++) {
                var o = -1;
                for (var j = 0; j < alt_vrts.Length; j++) {
                    if (alt_vrts[j] == ogl_vrts[i]) { o = j; break; }
                }
                vrt_rplc[i] = o;
            }

            for (var i = 0; i < alt_idcs.Length; i++) {
                alt_idcs[i] = vrt_rplc[ogl_idcs[i]];
            }
            alt_mesh.SetVertices(alt_vrts);
            alt_mesh.SetTriangles(alt_idcs, 0);
            alt_mesh.RecalculateBounds();
            alt_mesh.RecalculateNormals();
            alt_mesh.RecalculateTangents();
            return alt_mesh;
        }
    }
}
