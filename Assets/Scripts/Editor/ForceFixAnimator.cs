using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

[InitializeOnLoad]
public class ForceFixAnimator
{
    private static bool isExecuting = false;

    static ForceFixAnimator()
    {
        EditorApplication.delayCall += SihirliDuzeltme;
    }

    [MenuItem("Tools/Sihirli Düzeltme (Animasyon)")]
    public static void SihirliDuzeltme()
    {
        if (isExecuting) return;
        isExecuting = true;

        try
        {
            Debug.Log("🧙 [Sihirli Düzeltme] Animasyon sistemi taranıyor ve onarılıyor...");

            // 1. Assets/Hero/ klasöründeki tüm FBX dosyalarını bul
            string heroDir = "Assets/Hero";
            if (!Directory.Exists(heroDir))
            {
                Debug.LogError("❌ 'Assets/Hero' klasörü bulunamadı!");
                return;
            }

            string[] fbxFiles = Directory.GetFiles(heroDir, "*.fbx", SearchOption.AllDirectories);
            if (fbxFiles.Length == 0)
            {
                Debug.LogError("❌ 'Assets/Hero' klasöründe FBX dosyası bulunamadı!");
                return;
            }

            List<string> fbxPaths = new List<string>();
            foreach (var f in fbxFiles)
            {
                fbxPaths.Add(f.Replace('\\', '/'));
            }

            // 2. Idle FBX'ini bul
            string idleFbxPath = null;
            foreach (var p in fbxPaths)
            {
                if (p.ToLower().Contains("idle"))
                {
                    idleFbxPath = p;
                    break;
                }
            }
            if (idleFbxPath == null) idleFbxPath = fbxPaths[0];

            // Idle FBX'ini Humanoid yap
            EnsureHumanoid(idleFbxPath, null);

            // Idle Avatar'ı al
            Avatar heroAvatar = null;
            Object[] idleAssets = AssetDatabase.LoadAllAssetsAtPath(idleFbxPath);
            foreach (var a in idleAssets)
            {
                if (a is Avatar av)
                {
                    heroAvatar = av;
                    break;
                }
            }

            // Diğer FBX'leri de Humanoid yap ve Idle Avatar ile eşle
            foreach (var p in fbxPaths)
            {
                if (p != idleFbxPath)
                {
                    EnsureHumanoid(p, heroAvatar);
                }
            }

            // 3. Animasyon Kliplerini bul (Idle ve Run)
            AnimationClip idleClip = null;
            AnimationClip runClip = null;

            foreach (var p in fbxPaths)
            {
                string pLower = p.ToLower();
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(p);
                foreach (var asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                    {
                        Debug.Log($"Bulunan Klip: '{clip.name}' Dosya: {p}");
                        string nameLower = clip.name.ToLower();
                        if (idleClip == null && (pLower.Contains("idle") || nameLower.Contains("idle")))
                        {
                            idleClip = clip;
                            Debug.Log($"✔️ Idle Klibi seçildi: {clip.name} ({p})");
                        }
                        if (runClip == null && (pLower.Contains("run") || nameLower.Contains("run") || pLower.Contains("walk") || nameLower.Contains("walk")))
                        {
                            runClip = clip;
                            Debug.Log($"✔️ Run Klibi seçildi: {clip.name} ({p})");
                        }
                    }
                }
            }

            // Eğer hala Run bulunamadıysa herhangi bir ikinci klibi al
            if (runClip == null)
            {
                foreach (var p in fbxPaths)
                {
                    Object[] assets = AssetDatabase.LoadAllAssetsAtPath(p);
                    foreach (var asset in assets)
                    {
                        if (asset is AnimationClip clip && !clip.name.Contains("__preview__") && clip != idleClip)
                        {
                            runClip = clip;
                            break;
                        }
                    }
                    if (runClip != null) break;
                }
            }

            if (idleClip == null || runClip == null)
            {
                Debug.LogError($"❌ Klipler bulunamadı! Idle: {idleClip != null}, Run: {runClip != null}");
                return;
            }

            // 4. Animator Controller Oluştur / Yapılandır
            string controllerPath = "Assets/Hero/HeroAutoAnim.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }
            else
            {
                while (controller.layers.Length > 0)
                {
                    controller.RemoveLayer(0);
                }
                controller.AddLayer("Base Layer");
            }

            // IsRunning parametresi ekle
            bool hasParam = false;
            foreach (var param in controller.parameters)
            {
                if (param.name == "IsRunning") { hasParam = true; break; }
            }
            if (!hasParam)
            {
                controller.AddParameter("IsRunning", AnimatorControllerParameterType.Bool);
            }

            var sm = controller.layers[0].stateMachine;

            // Idle State
            AnimatorState idleState = sm.AddState("Idle");
            idleState.motion = idleClip;
            sm.defaultState = idleState;

            // Run State
            AnimatorState runState = sm.AddState("Run");
            runState.motion = runClip;

            // Transitions (Geçişler)
            var toRun = idleState.AddTransition(runState);
            toRun.AddCondition(AnimatorConditionMode.If, 0, "IsRunning");
            toRun.hasExitTime = false;
            toRun.duration = 0.15f;

            var toIdle = runState.AddTransition(idleState);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsRunning");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.15f;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("✔️ [HeroAutoAnim.controller] Başarıyla oluşturuldu ve klipler bağlandı!");

            // 5. Sahnedeki Karakteri Bul ve Bağla
            GameObject hero = GameObject.Find("Hero");
            if (hero == null) hero = GameObject.FindGameObjectWithTag("Player");
            if (hero == null)
            {
                foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude))
                {
                    if (go.name.Contains("Meshy") && go.GetComponentInChildren<Renderer>() != null)
                    {
                        hero = go;
                        break;
                    }
                }
            }

            if (hero != null)
            {
                hero.name = "Hero";
                hero.tag = "Player";

                Animator anim = hero.GetComponent<Animator>();
                if (anim == null) anim = hero.GetComponentInChildren<Animator>();
                if (anim == null) anim = hero.AddComponent<Animator>();

                anim.runtimeAnimatorController = controller;
                if (heroAvatar != null)
                {
                    anim.avatar = heroAvatar;
                }
                anim.applyRootMotion = false;
                EditorUtility.SetDirty(anim);

                CharacterController cc = hero.GetComponent<CharacterController>();
                if (cc == null) cc = hero.AddComponent<CharacterController>();
                cc.center = new Vector3(0, 1f, 0);
                cc.height = 2f;
                cc.radius = 0.4f;
                cc.minMoveDistance = 0f;
                EditorUtility.SetDirty(cc);

                HeroController hc = hero.GetComponent<HeroController>();
                if (hc == null) hc = hero.AddComponent<HeroController>();
                hc.idleAnimation = "Idle";
                hc.runAnimation = "Run";
                hc.moveSpeed = 15f;
                hc.rotationSpeed = 720f;
                hc.gravity = 35f;
                hc.weightMultiplier = 1.5f;
                hc.stickToGroundForce = 15f;
                hc.groundSnapDistance = 1.5f;
                hc.airControl = 0.35f;
                hc.enableCenterLimit = true;
                hc.maxDistancePercentage = 0.70f;
                EditorUtility.SetDirty(hc);

                // Kamera Takibi
                Camera cam = Camera.main;
                if (cam != null)
                {
                    CameraFollow cf = cam.GetComponent<CameraFollow>();
                    if (cf == null) cf = cam.gameObject.AddComponent<CameraFollow>();
                    cf.target = hero.transform;
                    cf.defaultDistance = 24f;
                    cf.minDistance = 14f;
                    cf.maxDistance = 34f;
                    cf.pitchAngle = 38f;
                    cf.clampYaw = false;
                    cf.minYawAngle = -70f;
                    cf.maxYawAngle = 70f;
                    EditorUtility.SetDirty(cf);
                }

                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("🎉 [SİHİRLİ DÜZELTME TAMAMLANDI] Hero nesnesi, Animator, Avatar ve Kamera hazırlandı ve sahne kaydedildi!");
            }
            else
            {
                Debug.LogWarning("⚠️ Sahnede Hero nesnesi bulunamadı.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("❌ Sihirli Düzeltme sırasında hata oluştu: " + ex);
        }
        finally
        {
            isExecuting = false;
        }
    }

    static void EnsureHumanoid(string fbxPath, Avatar sourceAvatar)
    {
        ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        bool needsReimport = false;

        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            if (sourceAvatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = sourceAvatar;
            }
            else
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            }
            needsReimport = true;
        }

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0)
        {
            clips = importer.defaultClipAnimations;
        }

        if (clips != null && clips.Length > 0)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                if (!clips[i].loopTime)
                {
                    clips[i].loopTime = true;
                    needsReimport = true;
                }
            }
            importer.clipAnimations = clips;
        }

        if (needsReimport)
        {
            importer.SaveAndReimport();
            Debug.Log($"✔️ Humanoid & Loop güncellendi: {fbxPath}");
        }
    }
}
