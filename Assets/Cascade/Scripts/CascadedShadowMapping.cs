using System.Collections.Generic;
using UnityEngine;

public class CascadedShadowMapping : MonoBehaviour
{
    [SerializeField] private Light _dirLight;
    [SerializeField] private Shader _shadowCaster;
    [SerializeField] private Camera _mainCamera;

    // 分割数
    private const int SplitsNum = 4;
    private Camera _dirLightCamera;
    // 影行列
    private readonly List<Matrix4x4> _world2ShadowMats = new List<Matrix4x4>(SplitsNum);
    // 分割したDirectionalLightカメラ
    private readonly GameObject[] _dirLightCameraSplits = new GameObject[SplitsNum];
    private readonly RenderTexture[] _depthTextures = new RenderTexture[SplitsNum];

    // ------------------視錐台関連------------------
    private float[] _lightSplitsNear;
    private float[] _lightSplitsFar;

    // 視錐台構造体
    private struct FrustumCorners
    {
        public Vector3[] NearCorners;
        public Vector3[] FarCorners;
    }

    private FrustumCorners[] _mainCameraSplitsFcs;
    private FrustumCorners[] _lightCameraSplitsFcs;
    // ------------------視錐台関連------------------

    private void Awake()
    {
        InitFrustumCorners();
        _dirLightCamera = CreateDirLightCamera();
        CreateRenderTexture();
    }

    /// <summary>
    /// 視錐台の初期化
    /// </summary>
    private void InitFrustumCorners()
    {
        _mainCameraSplitsFcs = new FrustumCorners[SplitsNum];
        _lightCameraSplitsFcs = new FrustumCorners[SplitsNum];
        for (var i = 0; i < SplitsNum; i++)
        {
            _mainCameraSplitsFcs[i].NearCorners = new Vector3[SplitsNum];
            _mainCameraSplitsFcs[i].FarCorners = new Vector3[SplitsNum];

            _lightCameraSplitsFcs[i].NearCorners = new Vector3[SplitsNum];
            _lightCameraSplitsFcs[i].FarCorners = new Vector3[SplitsNum];
        }
    }

    /// <summary>
    /// DirectionalLightカメラを生成
    /// </summary>
    /// <returns>生成したライト</returns>
    private Camera CreateDirLightCamera()
    {
        var goLightCamera = new GameObject("Directional Light Camera");
        var lightCamera = goLightCamera.AddComponent<Camera>();

        lightCamera.cullingMask = 1 << LayerMask.NameToLayer("Caster");
        lightCamera.backgroundColor = Color.white;
        lightCamera.clearFlags = CameraClearFlags.SolidColor;
        lightCamera.orthographic = true;
        lightCamera.enabled = false;

        for (var i = 0; i < SplitsNum; i++)
        {
            _dirLightCameraSplits[i] = new GameObject("dirLightCameraSplits" + i);
        }

        return lightCamera;
    }

     /// <summary>
     /// RenderTextureの生成
     /// </summary>
    private void CreateRenderTexture()
    {
        var rtFormat = RenderTextureFormat.ARGB32;
        if (!SystemInfo.SupportsRenderTextureFormat(rtFormat))
        {
            rtFormat = RenderTextureFormat.Default;
        }

        for (var i = 0; i < SplitsNum; i++)
        {
            // Stencilに書き込めるようにするので,Depthに24を指定
            _depthTextures[i] = new RenderTexture(1024, 1024, 24, rtFormat);
            Shader.SetGlobalTexture("_gShadowMapTexture" + i, _depthTextures[i]);
        }
    }

     private void Update()
    {
        // mainCameraを視錐台用に分割する
        CalcMainCameraSplitsFrustumCorners();
        // Light用Cameraの計算
        CalcLightCamera();

        if (!_dirLight || !_dirLightCamera)
        {
            return;
        }

        Shader.SetGlobalFloat("_gShadowBias", 0.005f);
        Shader.SetGlobalFloat("_gShadowStrength", 0.5f);

        // 影行列の計算
        CalcShadowMats();

        Shader.SetGlobalMatrixArray("_gWorld2Shadow", _world2ShadowMats);
    }



    /// <summary>
    /// mainCameraを視錐台用に分割する
    /// </summary>
    private void CalcMainCameraSplitsFrustumCorners()
    {
        var near = _mainCamera.nearClipPlane;
        var far = _mainCamera.farClipPlane;

        // UnityのCascadeSplitsの値
        // 0 : 6.7%, 1 : 13.3%, 2 : 26.7%, 4 : 53.5%,
        float[] nears =
        {
            near,
            far * 0.067f + near,
            far * 0.133f + far * 0.067f + near,
            far * 0.267f + far * 0.133f + far * 0.067f + near
        };
        float[] fars =
        {
            far * 0.067f + near,
            far * 0.133f + far * 0.067f + near,
            far * 0.267f + far * 0.133f + far * 0.067f + near,
            far
        };

        _lightSplitsNear = nears;
        _lightSplitsFar = fars;

        Shader.SetGlobalVector("_gLightSplitsNear", new Vector4(_lightSplitsNear[0], _lightSplitsNear[1], _lightSplitsNear[2], _lightSplitsNear[3]));
        Shader.SetGlobalVector("_gLightSplitsFar", new Vector4(_lightSplitsFar[0], _lightSplitsFar[1], _lightSplitsFar[2], _lightSplitsFar[3]));

        // 視錐台Cameraの頂点を計算
        for (var k = 0; k < SplitsNum; k++)
        {
            // near
            // ビューポート座標を指定して、指定したカメラ深度の4つの視錐台の頂点を指すビュー空間ベクトルを計算する。
            // 第4引数に視錐台ベクトルの配列が出力される
            _mainCamera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), _lightSplitsNear[k], Camera.MonoOrStereoscopicEye.Mono, _mainCameraSplitsFcs[k].NearCorners);
            for (var i = 0; i < SplitsNum; i++)
            {
                // 出力されたmainCameraの視錐コーナー座標をワールド座標に変換して、代入
                _mainCameraSplitsFcs[k].NearCorners[i] = _mainCamera.transform.TransformPoint(_mainCameraSplitsFcs[k].NearCorners[i]);
            }

            // far
            _mainCamera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), _lightSplitsFar[k], Camera.MonoOrStereoscopicEye.Mono, _mainCameraSplitsFcs[k].FarCorners);
            for (var i = 0; i < SplitsNum; i++)
            {
                _mainCameraSplitsFcs[k].FarCorners[i] = _mainCamera.transform.TransformPoint(_mainCameraSplitsFcs[k].FarCorners[i]);
            }
        }
    }



    /// <summary>
    /// Light用Cameraの計算
    /// </summary>
    private void CalcLightCamera()
    {
        if (_dirLightCamera == null)
        {
            return;
        }

        for (var k = 0; k < SplitsNum; k++)
        {
            for (var i = 0; i < SplitsNum; i++)
            {
                // mainCameraの視錐コーナー座標をローカルに変換し、視錐台Lightカメラに入れている
                _lightCameraSplitsFcs[k].NearCorners[i] = _dirLightCameraSplits[k].transform.InverseTransformPoint(_mainCameraSplitsFcs[k].NearCorners[i]);
                _lightCameraSplitsFcs[k].FarCorners[i] = _dirLightCameraSplits[k].transform.InverseTransformPoint(_mainCameraSplitsFcs[k].FarCorners[i]);
            }

            // 分割した視錐台の各座標で一番小さいもの、一番大きいものを計算
            float[] xs = { _lightCameraSplitsFcs[k].NearCorners[0].x, _lightCameraSplitsFcs[k].NearCorners[1].x, _lightCameraSplitsFcs[k].NearCorners[2].x, _lightCameraSplitsFcs[k].NearCorners[3].x,
                       _lightCameraSplitsFcs[k].FarCorners[0].x, _lightCameraSplitsFcs[k].FarCorners[1].x, _lightCameraSplitsFcs[k].FarCorners[2].x, _lightCameraSplitsFcs[k].FarCorners[3].x };

            float[] ys = { _lightCameraSplitsFcs[k].NearCorners[0].y, _lightCameraSplitsFcs[k].NearCorners[1].y, _lightCameraSplitsFcs[k].NearCorners[2].y, _lightCameraSplitsFcs[k].NearCorners[3].y,
                       _lightCameraSplitsFcs[k].FarCorners[0].y, _lightCameraSplitsFcs[k].FarCorners[1].y, _lightCameraSplitsFcs[k].FarCorners[2].y, _lightCameraSplitsFcs[k].FarCorners[3].y };

            float[] zs = { _lightCameraSplitsFcs[k].NearCorners[0].z, _lightCameraSplitsFcs[k].NearCorners[1].z, _lightCameraSplitsFcs[k].NearCorners[2].z, _lightCameraSplitsFcs[k].NearCorners[3].z,
                       _lightCameraSplitsFcs[k].FarCorners[0].z, _lightCameraSplitsFcs[k].FarCorners[1].z, _lightCameraSplitsFcs[k].FarCorners[2].z, _lightCameraSplitsFcs[k].FarCorners[3].z };

            var minX = Mathf.Min(xs);
            var maxX = Mathf.Max(xs);

            var minY = Mathf.Min(ys);
            var maxY = Mathf.Max(ys);

            var minZ = Mathf.Min(zs);
            var maxZ = Mathf.Max(zs);

            // 視錐台Lightカメラに視錐台の情報を入れる
            _lightCameraSplitsFcs[k].NearCorners[0] = new Vector3(minX, minY, minZ);
            _lightCameraSplitsFcs[k].NearCorners[1] = new Vector3(maxX, minY, minZ);
            _lightCameraSplitsFcs[k].NearCorners[2] = new Vector3(maxX, maxY, minZ);
            _lightCameraSplitsFcs[k].NearCorners[3] = new Vector3(minX, maxY, minZ);

            _lightCameraSplitsFcs[k].FarCorners[0] = new Vector3(minX, minY, maxZ);
            _lightCameraSplitsFcs[k].FarCorners[1] = new Vector3(maxX, minY, maxZ);
            _lightCameraSplitsFcs[k].FarCorners[2] = new Vector3(maxX, maxY, maxZ);
            _lightCameraSplitsFcs[k].FarCorners[3] = new Vector3(minX, maxY, maxZ);

            // near平面の中心点
            var pos = _lightCameraSplitsFcs[k].NearCorners[0] + (_lightCameraSplitsFcs[k].NearCorners[2] - _lightCameraSplitsFcs[k].NearCorners[0]) * 0.5f;

            // ローカルからワールドに変換
            _dirLightCameraSplits[k].transform.TransformPoint(pos);
            _dirLightCameraSplits[k].transform.rotation = _dirLight.transform.rotation;
        }
    }

    /// <summary>
    /// 影行列の計算
    /// </summary>
    private void CalcShadowMats()
    {
        _world2ShadowMats.Clear();
        for (var i = 0; i < SplitsNum; i++)
        {
            // DirectionalLightカメラの設定
            ConstructLightCameraSplits(i);
            _dirLightCamera.targetTexture = _depthTextures[i];

            // shaderの設定でカメラのレンダリングを行う
            _dirLightCamera.RenderWithShader(_shadowCaster, "");

            // カメラでレンダリングを行うので、false
            var projectionMatrix = GL.GetGPUProjectionMatrix(_dirLightCamera.projectionMatrix, false);
            // VP行列
            _world2ShadowMats.Add(projectionMatrix * _dirLightCamera.worldToCameraMatrix);
        }
    }

    /// <summary>
    /// DirectionalLightカメラの設定
    /// </summary>
    /// <param name="index">分割したDirectionalLightカメラのindex</param>
    private void ConstructLightCameraSplits(int index)
    {
        var cameraTransform = _dirLightCamera.transform;
        cameraTransform.position = _dirLightCameraSplits[index].transform.position;
        cameraTransform.rotation = _dirLightCameraSplits[index].transform.rotation;

        _dirLightCamera.nearClipPlane = _lightCameraSplitsFcs[index].NearCorners[0].z;
        _dirLightCamera.farClipPlane = _lightCameraSplitsFcs[index].FarCorners[0].z;

        _dirLightCamera.aspect = Vector3.Magnitude(_lightCameraSplitsFcs[index].NearCorners[0] - _lightCameraSplitsFcs[index].NearCorners[1]) / Vector3.Magnitude(_lightCameraSplitsFcs[index].NearCorners[1] - _lightCameraSplitsFcs[index].NearCorners[2]);
        _dirLightCamera.orthographicSize = Vector3.Magnitude(_lightCameraSplitsFcs[index].NearCorners[1] - _lightCameraSplitsFcs[index].NearCorners[2]) * 0.5f;
    }

    /// <summary>
    /// Gizmoの描画
    /// </summary>
    private void OnDrawGizmos()
    {
        if (_dirLightCamera == null)
        {
            return;
        }

        var fcs = new FrustumCorners[SplitsNum];
        for (var k = 0; k < SplitsNum; k++)
        {
            // mainCameraから見た、視錐台の分割線
            Gizmos.color = Color.white;
            Gizmos.DrawLine(_mainCameraSplitsFcs[k].NearCorners[1], _mainCameraSplitsFcs[k].NearCorners[2]);

            fcs[k].NearCorners = new Vector3[SplitsNum];
            fcs[k].FarCorners = new Vector3[SplitsNum];

            for (var i = 0; i < SplitsNum; i++)
            {
                fcs[k].NearCorners[i] = _dirLightCameraSplits[k].transform.TransformPoint(_lightCameraSplitsFcs[k].NearCorners[i]);
                fcs[k].FarCorners[i] = _dirLightCameraSplits[k].transform.TransformPoint(_lightCameraSplitsFcs[k].FarCorners[i]);
            }

            // 分割したDirectionalLightカメラの視錐台を描画
            // ライトのrotateを0にすれば、上のものと一致する
            Gizmos.color = Color.red;
            Gizmos.DrawLine(fcs[k].NearCorners[0], fcs[k].NearCorners[1]);
            Gizmos.DrawLine(fcs[k].NearCorners[1], fcs[k].NearCorners[2]);
            Gizmos.DrawLine(fcs[k].NearCorners[2], fcs[k].NearCorners[3]);
            Gizmos.DrawLine(fcs[k].NearCorners[3], fcs[k].NearCorners[0]);

            Gizmos.color = Color.green;
            Gizmos.DrawLine(fcs[k].FarCorners[0], fcs[k].FarCorners[1]);
            Gizmos.DrawLine(fcs[k].FarCorners[1], fcs[k].FarCorners[2]);
            Gizmos.DrawLine(fcs[k].FarCorners[2], fcs[k].FarCorners[3]);
            Gizmos.DrawLine(fcs[k].FarCorners[3], fcs[k].FarCorners[0]);

            Gizmos.DrawLine(fcs[k].NearCorners[0], fcs[k].FarCorners[0]);
            Gizmos.DrawLine(fcs[k].NearCorners[1], fcs[k].FarCorners[1]);
            Gizmos.DrawLine(fcs[k].NearCorners[2], fcs[k].FarCorners[2]);
            Gizmos.DrawLine(fcs[k].NearCorners[3], fcs[k].FarCorners[3]);
        }
    }

    private void OnDestroy()
    {
        _dirLightCamera = null;

        for (var i = 0; i < SplitsNum; i++)
        {
            if (_depthTextures[i])
            {
                DestroyImmediate(_depthTextures[i]);
            }
        }
    }
}
