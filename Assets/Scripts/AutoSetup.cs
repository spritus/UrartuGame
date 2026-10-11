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
                cf.minYawAngle = -70f;
                cf.maxYawAngle = 70f;
            }

            Animator anim = heroObj.GetComponent<Animator>();
            if (anim == null) anim = heroObj.GetComponentInChildren<Animator>();
            if (anim == null) anim = heroObj.AddComponent<Animator>();

            anim.applyRootMotion = false;

            CharacterController cc = heroObj.GetComponent<CharacterController>();
            if (cc == null) cc = heroObj.AddComponent<CharacterController>();
            cc.center = new Vector3(0, 1f, 0);
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.minMoveDistance = 0f;

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

#if UNITY_EDITOR
            // Controller kontrolü ve otomatik bağlama
            string controllerPath = "Assets/Hero/HeroAutoAnim.controller";
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            
            if (anim.runtimeAnimatorController == null && controller != null)
            {
                anim.runtimeAnimatorController = controller;
            }

            // Avatar kontrolü ve otomatik bağlama
            if (anim.avatar == null)
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
            Debug.Log("✔️ [AutoSetup] HeroController, Animator ve Kamera başarıyla hazırlandı!");
        }
    }
}
