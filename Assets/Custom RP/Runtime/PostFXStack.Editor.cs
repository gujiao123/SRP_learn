using UnityEditor;
using UnityEngine;

// me11: PostFXStack 的 Editor 部分
// 处理 Scene 窗口的后处理开关
partial class PostFXStack {

#if UNITY_EDITOR
    partial void ApplySceneViewState () {
        if (
            camera.cameraType == CameraType.SceneView &&
            !SceneView.currentDrawingSceneView.sceneViewState
                .showImageEffects
        ) {
            settings = null;
        }
    }
#endif
}

