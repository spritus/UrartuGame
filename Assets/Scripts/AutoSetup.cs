using UnityEngine;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

public class AutoSetup : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void SetupAutomatically()
    {
        // 1. KAMERA AYARLARI
        Camera mainCam = Camera.main;

        // 2. KAHRAMAN AYARLARI
        GameObject heroObj = null;
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);
        
        // Yanlışlıkla başka nesnelere eklenmiş HeroController'ları temizle
        foreach (var obj in allObjects)
        {
            HeroController strayHc = obj.GetComponent<HeroController>();
            if (strayHc != null && obj.name != "Hero" && !obj.name.Contains("Meshy"))
            {
                Object.DestroyImmediate(strayHc);
                CharacterController strayCc = obj.GetComponent<CharacterController>();
                if (strayCc != null) Object.DestroyImmediate(strayCc);
            }
        }

        // Asıl karakteri bul (Öncelikle "Hero" veya "Player" tag'i)
        foreach (var obj in allObjects)
        {
            if (obj.name == "Hero" || obj.CompareTag("Player"))
            {
                if (obj.GetComponentInChildren<Renderer>() != null)
                {
                    heroObj = obj; 
                    break;
                }
            }
        }

        if (heroObj == null)
        {
            foreach (var obj in allObjects)
            {
                if (obj.name.Contains("Meshy") && obj.GetComponentInChildren<Renderer>() != null)
                {
                    heroObj = obj;
                    break;
                }
            }
        }

        if (heroObj != null)
        {
            heroObj.name = "Hero";

            // Kamera takibini sağla
            if (mainCam != null)
            {
                CameraFollow cf = mainCam.GetComponent<CameraFollow>();
                if (cf == null) cf = mainCam.gameObject.AddComponent<CameraFollow>();
                cf.target = heroObj.transform;
                cf.defaultDistance = 24f;
                cf.minDistance = 14f;
                cf.maxDistance = 34f;
                cf.pitchAngle = 38f;
                cf.clampYaw = false;
                cf.minYawAngle = -70f;
                cf.maxYawAngle = 70f;
                cf.AlignBehindTarget();
            }

            Animator anim = heroObj.GetComponent<Animator>();
            if (anim == null) anim = heroObj.GetComponentInChildren<Animator>();
            if (anim == null) anim = heroObj.AddComponent<Animator>();

            anim.applyRootMotion = false;

            CharacterController cc = heroObj.GetComponent<CharacterController>();
            if (cc == null) cc = heroObj.AddComponent<CharacterController>();

            HeroController hc = heroObj.GetComponent<HeroController>();
            if (hc == null) hc = heroObj.AddComponent<HeroController>();
            hc.idleAnimation = "Idle";
            hc.runAnimation = "Run";
            hc.moveSpeed = 15f;
            hc.gravity = 35f;
            hc.weightMultiplier = 1.5f;
            hc.stickToGroundForce = 15f;
            hc.groundSnapDistance = 1.5f;
            hc.airControl = 0.35f;
            hc.enableCenterLimit = true;
            hc.maxDistancePercentage = 0.70f;
            hc.feetOffset = 0.04f;
            hc.skinWidth = 0.015f;
            hc.capsuleHeight = 2.0f;
            hc.capsuleRadius = 0.4f;
            hc.stepOffset = 0.4f;
            hc.ApplyCharacterControllerDimensions();

            // 1. Controller kontrolü ve bağlama (Hem Standalone EXE hem Editör)
            if (anim.runtimeAnimatorController == null)
            {
                RuntimeAnimatorController resCtrl = Resources.Load<RuntimeAnimatorController>("HeroAutoAnim");
                if (resCtrl != null)
                {
                    anim.runtimeAnimatorController = resCtrl;
                }
#if UNITY_EDITOR
                else
                {
                    string controllerPath = "Assets/Hero/HeroAutoAnim.controller";
                    RuntimeAnimatorController editorCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
                    if (editorCtrl != null) anim.runtimeAnimatorController = editorCtrl;
                }
#endif
            }

            // 2. Avatar kontrolü ve bağlama (Hem Standalone EXE hem Editör)
            if (anim.avatar == null)
            {
                Avatar resAvatar = Resources.Load<Avatar>("HeroAvatar");
                if (resAvatar != null)
                {
                    anim.avatar = resAvatar;
                }
#if UNITY_EDITOR
                else
                {
                    string[] fbxFiles = Directory.GetFiles("Assets/Hero", "*.fbx", SearchOption.AllDirectories);
                    foreach (var fbx in fbxFiles)
                    {
                        string p = fbx.Replace('\\', '/');
                        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(p);
                        foreach (var a in assets)
                        {
                            if (a is Avatar av)
                            {
                                anim.avatar = av;
                                break;
                            }
                        }
                        if (anim.avatar != null) break;
                    }
                }
#endif
            }

            // 3. MENÜ YÖNETİCİSİ KONTROLÜ
            GameMenuManager menu = Object.FindAnyObjectByType<GameMenuManager>();
            if (menu == null)
            {
                GameObject menuObj = new GameObject("GameMenuManager");
                menuObj.AddComponent<GameMenuManager>();
            }

            Debug.Log("✔️ [AutoSetup] HeroController, Animator, Kamera ve GameMenuManager başarıyla hazırlandı!");
        }
    }
}
