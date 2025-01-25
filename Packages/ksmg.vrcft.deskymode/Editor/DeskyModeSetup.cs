//#define ActualFinalIK
// uncomment above if using the actual FinalIK plugin from Rootmotion and not the stub!

using UnityEngine;
using RootMotion.FinalIK;
using RootMotion;
using System;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using System.Reflection;
using UnityEditor;
using static VRC.Dynamics.CollisionShapes;

namespace DeskyMode
{
    [Serializable]
    public class DeskyModeSetup
    {
        public const float STANDARD_HEIGHT = 1.7f;

        public BipedReferences references = new BipedReferences();

        public Material debugMat;

        public Transform deskyModeRoot { get; private set; }
        public Animator animator { get; private set; }
        public GameObject standardIKSet { get; private set; }
        // height scale is to account for actual height of the avatar, specifically for the animations
        public float avatarHeightScale { get; private set; }

        private float _debugElementSize = 1f;
        private float _avatarScale = 1f;
        private float _armatureScale = 1f;
        private float _reasonableOffset = 0f; 

    // === targets ===
    public Transform targetsRoot { get; private set; }
        // head targets
        public Transform headTargetsOrigin { get; private set; }
        public VRCParentConstraint headTargetsOriginConstraint { get; private set; }
        //public VRCPositionConstraint headTargetsOriginPosConstraint { get; private set; }
        //public VRCRotationConstraint headTargetsOriginRotConstraint { get; private set; }
        public Transform headPosTarget { get; private set; }
        public Transform headAimTarget { get; private set; }
        public Transform headAimPoleTargetOrigin { get; private set; }
        public Transform headAimPoleTarget { get; private set; }

        // hand targets (origin/target method in case want to animate position of hand target a bit in the future)
        public Transform leftHandTargetOrigin { get; private set; }
        public Transform leftHandTarget { get; private set; }
        public VRCParentConstraint leftHandTargetOriginConstraint { get; private set; }
        public Transform rightHandTargetOrigin { get; private set; }
        public Transform rightHandTarget { get; private set; }
        public VRCParentConstraint rightHandTargetOriginConstraint { get; private set; }

        // limb bend targets (to keep intended elbow position)
        public Transform leftArmBendTargetOrigin { get; private set; }

        public Transform leftArmBendTarget { get; private set; }
        public VRCParentConstraint leftArmBendTargetConstraint { get; private set; }
        public Transform rightArmBendTargetOrigin { get; private set; }

        public Transform rightArmBendTarget { get; private set; }
        public VRCParentConstraint rightArmBendTargetConstraint { get; private set; }

        // IK components
        private AimIK _aik;
        private AimIK _aik_head;
        private AimIK _aik_upperbody;
        private FullBodyBipedIK _headFbbik;

        private FullBodyBipedIK _handsFbbik;
        private LimbIK _leftHandlik;
        private LimbIK _rightHandlik;

        // initialize sequence
        //      using provided animator, populate internal reference
        //      generate IK points
        //      display mapping to user and IK point location to user
        //      enable/disable debug mesh renderers on targets (have them as children objects instead and destroy them?) as script option

        public DeskyModeSetup() { }

        public bool Initialize(VRCAvatarDescriptor avd, ref float ahs)
        {
            Animator avdanim = avd.GetComponent<Animator>();

            if (!avdanim.avatar.isHuman || !avdanim.avatar.isValid)
            {
                // TODO visible message to notify user that their avatar is borked
                return false;
            }
            animator = avdanim;

            // assume only avatar scale influencers are the base avatar scale and that they are uniform
            // also take into account potential scale in Armature (also assume uniform scale, individual bones are not scaled?)
            _avatarScale = animator.transform.localScale.x;
            _armatureScale = animator.GetBoneTransform(HumanBodyBones.Hips).parent.localScale.x;

            // set the avatarHeightScale from the given input
            SetAvatarHeightScale(ref ahs);

            // use VRC's approximated headcollider size for debug element approximate scale
            // these collider scales will be affected by armature (and any subsequent bone) scale, but not avatar scale
            // can use as is in anything under Desky Mode root, as that would have rescaled everything to be world scale
            _debugElementSize = avd.collider_head.radius * 2.5f * _armatureScale * _avatarScale / avatarHeightScale;

            return true;
        }

        private bool SetAvatarHeightScale(ref float ahs)
        {
            float headHeight = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.Head).position, Vector3.zero);
            // default height (size of person): 1.7m  
            if (ahs <= 0)
            {
                ahs = headHeight / STANDARD_HEIGHT;
                avatarHeightScale = ahs;
                _reasonableOffset = headHeight / avatarHeightScale;
                // TODO failure message? need to refactor lmao
                return false;
            }
            avatarHeightScale = ahs;
            // since everything under DeskyMode would have been scaled to take into account the avatarHeightScale, need to unscale for "absolute" position
            _reasonableOffset = headHeight / avatarHeightScale;

            return true;
        }

        private bool InitializeGimmickRoot()
        {
            if (animator == null) return false;

            // dumb name search method 
            Transform got = animator.transform.Find("DeskyMode");
            if (got)
            {
                // for now, delete supposedly old version
                UnityEngine.Object.DestroyImmediate(got.gameObject);
                Console.WriteLine("Destoyed old DeskyMode object");
            }

            deskyModeRoot = new GameObject("DeskyMode").transform; // may just grab in the VRCFury component or something idk
            deskyModeRoot.transform.parent = animator.transform;
            deskyModeRoot.transform.position = animator.transform.position;
            deskyModeRoot.transform.rotation = animator.transform.rotation;
            // by not setting this to one, are assured that any position is 1-to-1 with world unity
            // however, any local scales will need to take into account 
            deskyModeRoot.localScale *= avatarHeightScale;

            return true;
        }

        public bool GenerateIKTargets(bool addMeshRenderers=false)
        {
            if (deskyModeRoot == null && !InitializeGimmickRoot()) return false;

            targetsRoot = new GameObject("IK Targets").transform;
            targetsRoot.parent = deskyModeRoot;
            targetsRoot.position = deskyModeRoot.position;
            targetsRoot.rotation = deskyModeRoot.rotation;
            targetsRoot.localScale = Vector3.one;

            #region head 

            headTargetsOrigin = new GameObject("Head Origin").transform;
            headTargetsOrigin.parent = targetsRoot;
            headTargetsOrigin.position = references.head.position;  
            headTargetsOrigin.transform.rotation = references.head.rotation;
            headTargetsOrigin.transform.localScale = Vector3.one;

            headTargetsOriginConstraint = headTargetsOrigin.gameObject.AddComponent<VRCParentConstraint>();
            headTargetsOriginConstraint.enabled = true;
            headTargetsOriginConstraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource { Weight = 1, SourceTransform = animator.GetBoneTransform(HumanBodyBones.Head) });
            headTargetsOriginConstraint.RotationAtRest = Vector3.zero;
            headTargetsOriginConstraint.Locked = true;
            headTargetsOriginConstraint.ActivateConstraint();

            // separate position and rotation constraints to allow for nicer switching between rotation-only tracking
            // ^ above statement is pretty useless, as there are no situations where the head position is completely fixed...
            //headTargetsOriginPosConstraint = headTargetsOrigin.gameObject.AddComponent<VRCPositionConstraint>();
            //headTargetsOriginPosConstraint.enabled = true;
            //headTargetsOriginPosConstraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource { Weight = 1, SourceTransform = references.head });
            //headTargetsOriginPosConstraint.PositionAtRest = Vector3.zero;
            //headTargetsOriginPosConstraint.Locked = true;
            //headTargetsOriginPosConstraint.ActivateConstraint();

            //headTargetsOriginRotConstraint = headTargetsOrigin.gameObject.AddComponent<VRCRotationConstraint>();
            //headTargetsOriginRotConstraint.enabled = true;
            //headTargetsOriginRotConstraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource { Weight = 1, SourceTransform = references.head });
            //headTargetsOriginRotConstraint.RotationAtRest = Vector3.zero;
            //headTargetsOriginRotConstraint.Locked = true;
            //headTargetsOriginRotConstraint.ActivateConstraint();

            headPosTarget = new GameObject("Head Position Target").transform;
            headPosTarget.parent = headTargetsOrigin;
            headPosTarget.position = headTargetsOrigin.position;
            headPosTarget.rotation = headTargetsOrigin.rotation;
            headPosTarget.localScale = Vector3.one;

            headAimTarget = new GameObject("Head Aim Target").transform;
            headAimTarget.parent = headPosTarget;
            headAimTarget.localPosition = new Vector3(0, 0, _reasonableOffset); // offset aim target to in front of the head
            headAimTarget.rotation = headPosTarget.rotation;
            headAimTarget.localScale = Vector3.one;

            headAimPoleTargetOrigin = new GameObject("Head Aim Pole Target Origin").transform;
            headAimPoleTargetOrigin.parent = headTargetsOrigin;
            headAimPoleTargetOrigin.localPosition = new Vector3(0, _reasonableOffset / 1.2f, _reasonableOffset / 2.0f);
            headAimPoleTargetOrigin.rotation = headTargetsOrigin.rotation;
            headAimPoleTargetOrigin.localScale = Vector3.one;

            headAimPoleTarget = new GameObject("Head Aim Pole Target").transform;
            headAimPoleTarget.parent = headAimPoleTargetOrigin;
            headAimPoleTarget.position = headAimPoleTargetOrigin.position;
            headAimPoleTarget.rotation = headAimPoleTargetOrigin.rotation;
            headAimPoleTarget.localScale = Vector3.one;

            if (addMeshRenderers)
            {
                GameObject headPosTargetVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                headPosTargetVis.name = "Vis";
                headPosTargetVis.transform.parent = headPosTarget;
                headPosTargetVis.transform.position = headPosTarget.position;
                headPosTargetVis.transform.rotation = headPosTarget.rotation;
                headPosTargetVis.transform.localScale = Vector3.one * _debugElementSize;
                UnityEngine.Object.DestroyImmediate(headPosTargetVis.GetComponent<BoxCollider>());
                GameObject headAimTargetVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                headAimTargetVis.name = "Vis";
                headAimTargetVis.transform.parent = headAimTarget;
                headAimTargetVis.transform.position = headAimTarget.position;
                headAimTargetVis.transform.rotation = headAimTarget.rotation;
                headAimTargetVis.transform.localScale = Vector3.one * _debugElementSize * 0.5f;
                UnityEngine.Object.DestroyImmediate(headAimTargetVis.GetComponent<SphereCollider>());
                GameObject headAimPoleTargetVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                headAimPoleTargetVis.name = "Vis";
                headAimPoleTargetVis.transform.parent = headAimPoleTarget;
                headAimPoleTargetVis.transform.position = headAimPoleTarget.position;
                headAimPoleTargetVis.transform.rotation = headAimPoleTarget.rotation;
                headAimPoleTargetVis.transform.localScale = Vector3.one * _debugElementSize * 0.25f;
                UnityEngine.Object.DestroyImmediate(headAimPoleTargetVis.GetComponent<SphereCollider>());

                //Material debugMat = AssetDatabase.LoadAssetAtPath<Material>(debugMatPath);
                headPosTargetVis.GetComponent<Renderer>().material = debugMat;
                headAimTargetVis.GetComponent <Renderer>().material = debugMat;
                headAimPoleTargetVis.GetComponent<Renderer>().material = debugMat;
            }

            #endregion

            #region hands

            // hand targets are only (currently?) used for pinning hands in static animations

            leftHandTargetOrigin = new GameObject("Left Hand Target Origin").transform;
            leftHandTargetOrigin.parent = targetsRoot;
            leftHandTargetOrigin.position = references.leftHand.position;
            leftHandTargetOrigin.rotation = references.leftHand.rotation;
            leftHandTargetOrigin.localScale = Vector3.one;

            leftHandTargetOriginConstraint = leftHandTargetOrigin.gameObject.AddComponent<VRCParentConstraint>();
            leftHandTargetOriginConstraint.enabled = true;
            leftHandTargetOriginConstraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource { Weight = 1, SourceTransform = references.leftHand });
            leftHandTargetOriginConstraint.RotationAtRest = Vector3.zero;
            //leftHandTargetOriginConstraint.PositionAtRest = Vector3.zero;
            leftHandTargetOriginConstraint.Locked = true;
            leftHandTargetOriginConstraint.ActivateConstraint();

            leftHandTarget = new GameObject("Left Hand Target").transform;
            leftHandTarget.parent = leftHandTargetOrigin;
            leftHandTarget.position = leftHandTargetOrigin.position;
            leftHandTarget.rotation = leftHandTargetOrigin.rotation;
            leftHandTarget.localScale = Vector3.one;

            rightHandTargetOrigin = new GameObject("Right Hand Target Origin").transform;
            rightHandTargetOrigin.parent = targetsRoot;
            rightHandTargetOrigin.position = references.rightHand.position;
            rightHandTargetOrigin.rotation = references.rightHand.rotation;
            rightHandTargetOrigin.localScale = Vector3.one;

            rightHandTargetOriginConstraint = rightHandTargetOrigin.gameObject.AddComponent<VRCParentConstraint>();
            rightHandTargetOriginConstraint.enabled = true;
            rightHandTargetOriginConstraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource { Weight = 1, SourceTransform = references.rightHand });
            rightHandTargetOriginConstraint.RotationAtRest = Vector3.zero;
            //leftHandTargetOriginConstraint.PositionAtRest = Vector3.zero;
            rightHandTargetOriginConstraint.Locked = true;
            rightHandTargetOriginConstraint.ActivateConstraint();

            rightHandTarget = new GameObject("Right Hand Target").transform;
            rightHandTarget.parent = rightHandTargetOrigin;
            rightHandTarget.position = rightHandTargetOrigin.position;
            rightHandTarget.rotation = rightHandTargetOrigin.rotation;
            rightHandTarget.localScale = Vector3.one;

            // vis for hand
            if (addMeshRenderers)
            {
                GameObject leftHandTargetVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leftHandTargetVis.name = "Vis";
                leftHandTargetVis.transform.parent = leftHandTarget;
                leftHandTargetVis.transform.position = leftHandTarget.position;
                leftHandTargetVis.transform.rotation = leftHandTarget.rotation;
                // no scale in parent, so localScale should match to absolute scale if taking into account avatar scale
                leftHandTargetVis.transform.localScale = Vector3.one * _debugElementSize * 0.5f;
                UnityEngine.Object.DestroyImmediate(leftHandTargetVis.GetComponent<BoxCollider>());

                GameObject rightHandTargetVis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rightHandTargetVis.name = "Vis";
                rightHandTargetVis.transform.parent = rightHandTarget;
                rightHandTargetVis.transform.position = rightHandTarget.position;
                rightHandTargetVis.transform.rotation = rightHandTarget.rotation;
                // no scale in parent, so localScale should match to absolute scale if taking into account avatar scale
                rightHandTargetVis.transform.localScale = Vector3.one * _debugElementSize * 0.5f;
                UnityEngine.Object.DestroyImmediate(rightHandTargetVis.GetComponent<BoxCollider>());

                //Material debugMat = AssetDatabase.LoadAssetAtPath<Material>(debugMatPath);
                leftHandTargetVis.GetComponent<Renderer>().material = debugMat;
                rightHandTargetVis.GetComponent<Renderer>().material = debugMat;
            }

            #endregion

            #region armbend

            // left arm bend
            leftArmBendTargetOrigin = new GameObject("Left Arm Bend Origin").transform;
            leftArmBendTargetOrigin.parent = targetsRoot.transform;
            leftArmBendTargetOrigin.position = references.leftForearm.position;
            leftArmBendTargetOrigin.rotation = references.leftForearm.rotation;
            leftArmBendTargetOrigin.localScale = Vector3.one;
            leftArmBendTarget = new GameObject("Left Arm Bend Target").transform;
            leftArmBendTarget.parent = leftArmBendTargetOrigin.transform;
            leftArmBendTarget.localPosition = new Vector3(0, 0, 0.01f * (_reasonableOffset / _avatarScale));
            leftArmBendTarget.rotation = leftArmBendTargetOrigin.rotation;
            leftArmBendTarget.localScale = Vector3.one;

            leftArmBendTargetConstraint = leftArmBendTargetOrigin.gameObject.AddComponent<VRCParentConstraint>();
            leftArmBendTargetConstraint.enabled = true;
            leftArmBendTargetConstraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource { Weight = 1, SourceTransform = references.leftForearm });
            leftArmBendTargetConstraint.RotationAtRest = Vector3.zero;
            //leftArmBendTargetConstraint.PositionAtRest = Vector3.zero;
            leftArmBendTargetConstraint.Locked = true;
            leftArmBendTargetConstraint.ActivateConstraint();

            // right arm bend
            rightArmBendTargetOrigin = new GameObject("Right Arm Bend Origin").transform;
            rightArmBendTargetOrigin.parent = targetsRoot.transform;
            rightArmBendTargetOrigin.position = references.rightForearm.position;
            rightArmBendTargetOrigin.rotation = references.rightForearm.rotation;
            rightArmBendTargetOrigin.localScale = Vector3.one;
            rightArmBendTarget = new GameObject("Right Arm Bend Target").transform;
            rightArmBendTarget.parent = rightArmBendTargetOrigin.transform;
            rightArmBendTarget.localPosition = new Vector3(0, 0, 0.01f * (_reasonableOffset / _avatarScale));
            rightArmBendTarget.rotation = rightArmBendTargetOrigin.rotation;
            rightArmBendTarget.localScale = Vector3.one;

            rightArmBendTargetConstraint = rightArmBendTargetOrigin.gameObject.AddComponent<VRCParentConstraint>();
            rightArmBendTargetConstraint.enabled = true;
            rightArmBendTargetConstraint.Sources.Add(new VRC.Dynamics.VRCConstraintSource { Weight = 1, SourceTransform = references.rightForearm });
            rightArmBendTargetConstraint.RotationAtRest = Vector3.zero;
            //leftArmBendTargetConstraint.PositionAtRest = Vector3.zero;
            rightArmBendTargetConstraint.Locked = true;
            rightArmBendTargetConstraint.ActivateConstraint();

            if (addMeshRenderers)
            {
                GameObject leftArmBendTargetVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leftArmBendTargetVis.name = "Vis";
                leftArmBendTargetVis.transform.parent = leftArmBendTarget;
                leftArmBendTargetVis.transform.position = leftArmBendTarget.position;
                leftArmBendTargetVis.transform.rotation = leftArmBendTarget.rotation;
                // already removed avatar scale
                leftArmBendTargetVis.transform.localScale = Vector3.one * _debugElementSize * 0.5f;
                UnityEngine.Object.DestroyImmediate(leftArmBendTargetVis.GetComponent<SphereCollider>());

                GameObject rightArmBendTargetVis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rightArmBendTargetVis.name = "Vis";
                rightArmBendTargetVis.transform.parent = rightArmBendTarget;
                rightArmBendTargetVis.transform.position = rightArmBendTarget.position;
                rightArmBendTargetVis.transform.rotation = rightArmBendTarget.rotation;
                rightArmBendTargetVis.transform.localScale = Vector3.one * _debugElementSize * 0.5f;
                UnityEngine.Object.DestroyImmediate(rightArmBendTargetVis.GetComponent<SphereCollider>());

                leftArmBendTargetVis.GetComponent<Renderer>().material = debugMat;
                rightArmBendTargetVis.GetComponent <Renderer>().material = debugMat;
            }

            #endregion

            return true;
        }

        // Automatically set the references in our Biped References using the humanoid avatar description
        // Follows how Final IK actually does it in (AssignHumanoidReferences), without their checks (we assume the avatar was properly set up for VRC) 
        public bool AutoPopulateReferences()
        {
            return AutoPopulateReferences(ref references);
        }
        private bool AutoPopulateReferences(ref BipedReferences references)
        {
            if (animator == null) return false;

            // reinitialize references
            references = new BipedReferences();

            references.root = animator.gameObject.transform;

            references.head = animator.GetBoneTransform(HumanBodyBones.Head);

            references.leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            references.leftCalf = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            references.leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

            references.rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            references.rightCalf = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            references.rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            references.leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            references.leftForearm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            references.leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

            references.rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            references.rightForearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            references.rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            references.pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);

            AddBoneToHierarchy(ref references.spine, animator.GetBoneTransform(HumanBodyBones.Spine));
            AddBoneToHierarchy(ref references.spine, animator.GetBoneTransform(HumanBodyBones.Chest));
            // add upperchest if it exists?
            if (animator.GetBoneTransform(HumanBodyBones.UpperChest) != null) AddBoneToHierarchy(ref references.spine, animator.GetBoneTransform(HumanBodyBones.UpperChest));
            AddBoneToHierarchy(ref references.spine, animator.GetBoneTransform(HumanBodyBones.Neck));
            AddBoneToHierarchy(ref references.spine, animator.GetBoneTransform(HumanBodyBones.Head));

            // TODO: implement similar warning 
            //-- from RootMotion.BipedReferences AutoDetectReferences
            //if (!references.isFilled)
            //{
            //    Warning.Log("BipedReferences contains one or more missing Transforms.", root, true);
            //    return false;
            //}
            return false;
        }

        // Adds transform to hierarchy if not null
        //      currently stolen directly from RootMotion.BipedReference
        //      (honestly not sure how to "rewrite" this function..)
        private static void AddBoneToHierarchy(ref Transform[] bones, Transform transform)
        {
            if (transform == null) return;

            Array.Resize(ref bones, bones.Length + 1);
            bones[bones.Length - 1] = transform;
        }

        private bool ReadyForIK()
        {
            if (deskyModeRoot &&
                references.root &&
                headPosTarget && 
                headAimTarget && 
                headAimPoleTarget) return true;
            return false;
        }

        private GameObject InitializeStandardGameObject()
        {
            if (!deskyModeRoot) return null;
            if (standardIKSet)
            {
                return standardIKSet;
            } else
            {
                Transform got = deskyModeRoot.Find("Standard IK");
                if (got)
                {
                    standardIKSet = got.gameObject;
                    return got.gameObject;
                }
            }

            GameObject go = new GameObject("Standard IK");
            go.transform.parent = deskyModeRoot;
            go.transform.position = deskyModeRoot.position;
            go.transform.rotation = deskyModeRoot.rotation;
            standardIKSet = go;
            return go;
        }

        bool CreateVRIK()
        {
            // TODO
            return false;
        }

        public bool CreateAimIKs()
        {
            _aik = CreateAimIK();
            _aik_head = CreateAimIK(0f, 0.2f, 0.2f, 0.8f, 1f);
            _aik_upperbody = CreateAimIK(0.7f, 1f, 1f, 0.05f, 0.1f);
            if (_aik != null && _aik_head != null && _aik_upperbody != null) return true;
            return false;
        }

        // use references to properly fill out AimIK 
        // default spine bone weight is low to emphasize head movement
        public AimIK CreateAimIK(float spineW = 0.3f, float chestW = 0.42f, float uChestW = 0.33f, float neckW = 0.8f, float headW = 1f)
        {
            if (!ReadyForIK()) return null;
            var go = InitializeStandardGameObject();
            if (!go) return null;

            //// for now: get rid of a potentially existing component
            //AimIK aik = go.GetComponent<AimIK>();
            //if (aik) UnityEngine.Object.DestroyImmediate(aik);

            AimIK aik = go.AddComponent<AimIK>();
            IKSolverAim aiks = aik.solver;

            // is this necessary? Does this matter???
            aiks.IKPosition = deskyModeRoot.position;
            // "master weight of the solver"
            aiks.IKPositionWeight = 1f;
            // funny root handling
            FieldInfo field = typeof(RootMotion.FinalIK.IKSolverAim).GetField
                ("root", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(aiks, (Transform)aik.transform);

            // standard solver stuff
            aik.fixTransforms = true;
            aiks.tolerance = 0.18f; // tolerance helps in weird default pose positions
            aiks.maxIterations = 6; // nicer solving
            aiks.useRotationLimits = false;
            aiks.XY = false; // we aren't 2D..

            // targets
            aiks.target = headAimTarget;
            aiks.poleTarget = headAimPoleTarget;
            aiks.poleWeight = 1f;
            // pole target position will be set to poleTarget by FIK
            // axis and pole axis defaults OK

            // the primary transform to aim
            aiks.transform = references.head;

            // set transform bone chain to spine chain bones
            // possibility for customization here: only use X-number of the top spine bones? easy customizable weights?
            aiks.bones = new IKSolver.Bone[references.spine.Length];
            for (int i = 0; i < references.spine.Length; i++)
            {
                aiks.bones[i] = new IKSolver.Bone();
                aiks.bones[i].transform = references.spine[i].transform;
                if (aiks.bones[i].transform == animator.GetBoneTransform(HumanBodyBones.Spine))
                {
                    aiks.bones[i].weight = spineW;
                }
                else if (aiks.bones[i].transform == animator.GetBoneTransform(HumanBodyBones.Chest))
                {
                    aiks.bones[i].weight = chestW;
                }
                else if (aiks.bones[i].transform == animator.GetBoneTransform(HumanBodyBones.UpperChest))
                {
                    aiks.bones[i].weight = uChestW;
                }
                else if (aiks.bones[i].transform == animator.GetBoneTransform(HumanBodyBones.Neck))
                {
                    aiks.bones[i].weight = neckW;
                }
                else if (aiks.bones[i].transform == animator.GetBoneTransform(HumanBodyBones.Head))
                {
                    aiks.bones[i].weight = headW;
                }
            }
            // little extra on the top
            aiks.bones[aiks.bones.Length - 3].weight = 0.33f;

            // component disabled if using IKExecutionOrder
            aik.enabled = false;

            // force the real Final IK to initialize component internals in VRChat (really hoping that it initializes before any other VRC system changes things)
            // would be a huge pain in the ass to attempt to reimplement it all for fbbik as it uses a bunch of sub IKs... 
            // use firstInitiation and initiated in IKSolver
            //_aik = aik;
            return aik;
        }

        // use references to properly fill out FullBodyBipedIK
        public bool CreateHeadFullBodyBipedIK()
        {
            if (!ReadyForIK()) return false;
            var go = InitializeStandardGameObject();
            if (!go) return false;

            // for now: get rid of a potentially existing component
            //FullBodyBipedIK fbbik = go.GetComponent<FullBodyBipedIK>();
            //if (fbbik) UnityEngine.Object.DestroyImmediate(fbbik);

            FullBodyBipedIK fbbik = go.AddComponent<FullBodyBipedIK>();
            IKSolverFullBodyBiped fbbiks = fbbik.solver;

            // set up the internal BipedReferences using avatar animator
            AutoPopulateReferences(ref fbbik.references);
            // set the solver rootNode to the head (because head tracking duh)
            fbbiks.rootNode = references.head;
            // not 100% weight?
            fbbiks.IKPositionWeight = 0.87f;

            // set (private) solver root using funny component field method to avatar root(?)
            FieldInfo field = typeof(RootMotion.FinalIK.IKSolverFullBodyBiped).GetField
                ("root", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(fbbiks, (Transform)references.root);

            // === IKSolverFullBody property filling ===

            // bit more number of solver iterations
            fbbiks.iterations = 6;

            // chain | root node chains (head, left / right arms, left / right legs)
            // the only chain with Child constraints is the head (actually the body / solver root) 
            // pin 0 pull 1 default, setting nodes manually to the fbbik default (all default values except transform)
            FBIKChain headChain = new FBIKChain();
            headChain.pin = 0f; headChain.pull = 1f;
            IKSolver.Node headChainNode = new IKSolver.Node();
            headChainNode.transform = references.head;
            headChain.nodes = new IKSolver.Node[] { headChainNode };

            // children chains are the following 1,2,3,4 chains 
            headChain.children = new int[] { 1, 2, 3, 4 };

            // child chain constraints (not sure why these are how they are but they are) 
            // indexes 1, 2 have 1 on pull elasticity, otherwise 0
            FBIKChain.ChildConstraint[] headCCs = new FBIKChain.ChildConstraint[4];

            // get the bone1/bone2 fields of the ChildConstraint
            FieldInfo bone1 = typeof(RootMotion.FinalIK.FBIKChain.ChildConstraint).GetField
                ("bone1", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo bone2 = typeof(RootMotion.FinalIK.FBIKChain.ChildConstraint).GetField
                ("bone2", BindingFlags.Instance | BindingFlags.NonPublic);

            // FBIKChain.ChildConstraint doesn't have a constructor in stub, but does have a 4 argument instructor in actual
#if ActualFinalIK
            headCCs[0] = new FBIKChain.ChildConstraint(references.leftUpperArm, references.rightThigh, 0f, 1f);
#else
            headCCs[0] = new FBIKChain.ChildConstraint();
#endif
            headCCs[0].pushElasticity = 0f;
            headCCs[0].pullElasticity = 1f;
            bone1.SetValue(headCCs[0], (Transform)references.leftUpperArm);
            bone2.SetValue(headCCs[0], (Transform)references.rightThigh);

#if ActualFinalIK
            headCCs[1] = new FBIKChain.ChildConstraint(references.rightUpperArm, references.leftThigh, 0f, 1f);
#else
            headCCs[1] = new FBIKChain.ChildConstraint();
#endif
            headCCs[1].pushElasticity = 0f;
            headCCs[1].pullElasticity = 1f;
            bone1.SetValue(headCCs[1], (Transform)references.rightUpperArm);
            bone2.SetValue(headCCs[1], (Transform)references.leftThigh);

#if ActualFinalIK
            headCCs[2] = new FBIKChain.ChildConstraint(references.leftUpperArm, references.rightUpperArm, 0f, 0f);
#else
            headCCs[2] = new FBIKChain.ChildConstraint();
#endif
            headCCs[2].pushElasticity = 0f;
            headCCs[2].pullElasticity = 0f;
            bone1.SetValue(headCCs[2], (Transform)references.leftUpperArm);
            bone2.SetValue(headCCs[2], (Transform)references.rightUpperArm);

#if ActualFinalIK
            headCCs[3] = new FBIKChain.ChildConstraint(references.leftThigh, references.rightThigh, 0f, 0f);
#else
            headCCs[3] = new FBIKChain.ChildConstraint();
#endif
            headCCs[3].pushElasticity = 0f;
            headCCs[3].pullElasticity = 0f;
            bone1.SetValue(headCCs[3], (Transform)references.leftThigh);
            bone2.SetValue(headCCs[3], (Transform)references.rightThigh);

            headChain.childConstraints = headCCs;

            FBIKChain leftArmChain = new FBIKChain();
            leftArmChain.pin = 0f; leftArmChain.pull = 1f;
            leftArmChain.nodes = new IKSolver.Node[3];
            leftArmChain.nodes[0] = new IKSolver.Node();
            leftArmChain.nodes[0].transform = references.leftUpperArm;
            leftArmChain.nodes[1] = new IKSolver.Node();
            leftArmChain.nodes[1].transform = references.leftForearm;
            leftArmChain.nodes[2] = new IKSolver.Node();
            leftArmChain.nodes[2].transform = references.leftHand;

            FBIKChain rightArmChain = new FBIKChain();
            rightArmChain.pin = 0f; rightArmChain.pull = 1f;
            rightArmChain.nodes = new IKSolver.Node[3];
            rightArmChain.nodes[0] = new IKSolver.Node();
            rightArmChain.nodes[0].transform = references.rightUpperArm;
            rightArmChain.nodes[1] = new IKSolver.Node();
            rightArmChain.nodes[1].transform = references.rightForearm;
            rightArmChain.nodes[2] = new IKSolver.Node();
            rightArmChain.nodes[2].transform = references.rightHand;

            FBIKChain leftLegChain = new FBIKChain();
            leftLegChain.pin = 0f; leftLegChain.pull = 1f;
            leftLegChain.nodes = new IKSolver.Node[3];
            leftLegChain.nodes[0] = new IKSolver.Node();
            leftLegChain.nodes[0].transform = references.leftThigh;
            leftLegChain.nodes[1] = new IKSolver.Node();
            leftLegChain.nodes[1].transform = references.leftCalf;
            leftLegChain.nodes[2] = new IKSolver.Node();
            leftLegChain.nodes[2].transform = references.leftFoot;

            FBIKChain rightLegChain = new FBIKChain();
            rightLegChain.pin = 0f; rightLegChain.pull = 1f;
            rightLegChain.nodes = new IKSolver.Node[3];
            rightLegChain.nodes[0] = new IKSolver.Node();
            rightLegChain.nodes[0].transform = references.rightThigh;
            rightLegChain.nodes[1] = new IKSolver.Node();
            rightLegChain.nodes[1].transform = references.rightCalf;
            rightLegChain.nodes[2] = new IKSolver.Node();
            rightLegChain.nodes[2].transform = references.rightFoot;

            fbbiks.chain = new FBIKChain[] { headChain, leftArmChain, rightArmChain, leftLegChain, rightLegChain };

            // effectors | standard fbbik 9 
            // we only care about body(head) effector
            // position and rotation defaults are AOK

            Transform[] effectorsOrder = new Transform[] {
                references.head,
                references.leftUpperArm,
                references.rightUpperArm,
                references.leftThigh,
                references.rightThigh,
                references.leftHand,
                references.rightHand,
                references.leftFoot,
                references.rightFoot
            };

            fbbiks.effectors = new IKEffector[9];

            IKEffector headEffector = new IKEffector();
            headEffector.target = headPosTarget;
            headEffector.bone = references.head;
            headEffector.positionWeight = 1f;
            headEffector.rotationWeight = 0f;
            headEffector.maintainRelativePositionWeight = 0f;
            // why? because it's supposed to be body I guess
            headEffector.childBones = new Transform[] { references.leftThigh, references.rightThigh };

            fbbiks.effectors[0] = headEffector;
            for (int i = 1; i < 9; i++)
            {
                fbbiks.effectors[i] = new IKEffector();
                fbbiks.effectors[i].bone = effectorsOrder[i];
                fbbiks.effectors[i].positionWeight = 0f;
                fbbiks.effectors[i].rotationWeight = 0f;
                fbbiks.effectors[i].maintainRelativePositionWeight = 0f;
            }
            // set hand and feet effector plane bones
            // left hand
            fbbiks.effectors[5].planeBone1 = references.leftUpperArm;
            fbbiks.effectors[5].planeBone2 = references.rightUpperArm;
            fbbiks.effectors[5].planeBone3 = references.head;

            fbbiks.effectors[6].planeBone1 = references.rightUpperArm;
            fbbiks.effectors[6].planeBone2 = references.leftUpperArm;
            fbbiks.effectors[6].planeBone3 = references.head;

            fbbiks.effectors[7].planeBone1 = references.leftThigh;
            fbbiks.effectors[7].planeBone2 = references.rightThigh;
            fbbiks.effectors[7].planeBone3 = references.head;

            fbbiks.effectors[8].planeBone1 = references.rightThigh;
            fbbiks.effectors[8].planeBone2 = references.leftThigh;
            fbbiks.effectors[8].planeBone3 = references.head;

            // spineMapping
            // first, set Transform[] spineBones and *include* hip (thus +1)
            fbbiks.spineMapping.spineBones = new Transform[references.spine.Length + 1];
            //Array.Copy(references.spine, fbbiks.spineMapping.spineBones, references.spine.Length);
            fbbiks.spineMapping.spineBones[0] = references.pelvis;
            for (int i = 1; i < references.spine.Length + 1; i++)
            {
                fbbiks.spineMapping.spineBones[i] = references.spine[i - 1];
            }

            // set spinemapping for first limb bones off spine
            fbbiks.spineMapping.leftUpperArmBone = references.leftUpperArm;
            fbbiks.spineMapping.rightUpperArmBone = references.rightUpperArm;
            fbbiks.spineMapping.leftThighBone = references.leftThigh;
            fbbiks.spineMapping.rightThighBone = references.rightThigh;

            // spinemapping settings
            fbbiks.spineMapping.twistWeight = 0.2f;

            // boneMappings | head is the only "individual bone" for the solver 
            RootMotion.FinalIK.IKMappingBone headMappingBone = new RootMotion.FinalIK.IKMappingBone(references.head);
            headMappingBone.maintainRotationWeight = 1; // this could maybe be 1 if fbbik is applied after Aim
            fbbiks.boneMappings = new RootMotion.FinalIK.IKMappingBone[] { headMappingBone };

            // limbMapping | standard biped stuff
            // we don't actually want any of these IK limbs to affect the avatar (for now?) so weights = 0

            IKMappingLimb leftArmLimb = new RootMotion.FinalIK.IKMappingLimb();
            leftArmLimb.maintainRotationWeight = 0;
            leftArmLimb.weight = 0;
            leftArmLimb.parentBone = references.leftUpperArm.parent; // this is fine
            leftArmLimb.bone1 = references.leftUpperArm;
            leftArmLimb.bone2 = references.leftForearm;
            leftArmLimb.bone3 = references.leftHand;
            IKMappingLimb rightArmLimb = new RootMotion.FinalIK.IKMappingLimb();
            rightArmLimb.maintainRotationWeight = 0;
            rightArmLimb.weight = 0;
            rightArmLimb.parentBone = references.rightUpperArm.parent; // this is fine
            rightArmLimb.bone1 = references.rightUpperArm;
            rightArmLimb.bone2 = references.rightForearm;
            rightArmLimb.bone3 = references.rightHand;
            // weight and maintainRotationWeight were 1 for the leg limbs on the reference gote.. so...
            IKMappingLimb leftLegLimb = new RootMotion.FinalIK.IKMappingLimb();
            leftLegLimb.maintainRotationWeight = 1;
            leftLegLimb.weight = 1;
            //leftLegLimb.parentBone = // leave parent bone for legs as null
            leftLegLimb.bone1 = references.leftThigh;
            leftLegLimb.bone2 = references.leftCalf;
            leftLegLimb.bone3 = references.leftFoot;
            IKMappingLimb rightLegLimb = new RootMotion.FinalIK.IKMappingLimb();
            rightLegLimb.maintainRotationWeight = 1;
            rightLegLimb.weight = 1;
            //leftLegLimb.parentBone = // leave parent bone for legs as null
            rightLegLimb.bone1 = references.rightThigh;
            rightLegLimb.bone2 = references.rightCalf;
            rightLegLimb.bone3 = references.rightFoot;

            fbbiks.limbMappings = new RootMotion.FinalIK.IKMappingLimb[] { leftArmLimb,  rightArmLimb, leftLegLimb, rightLegLimb };

            // FABRIK pass | don't actually care as we don't have effectors on the limbs, but leave default.. 
            fbbiks.FABRIKPass = true; // shouldn't have a difference if false..

            // some arbitrary values
            fbbiks.spineStiffness = 0.35f;
            //fbbiks.pullBodyHorizontal = 0.1f;

            // component disabled if using IKExecutionOrder
            fbbik.enabled = false;

            // primary reference from BipedIKSolvers
            //public void AssignReferences(BipedReferences references)

            /// <summary>
            /// Sets up the solver to BipedReferences and reinitiates (if in runtime).
            /// </summary>
            /// <param name="references">Biped references.</param>
            /// <param name="rootNode">Root node (optional). if null, will try to detect the root node bone automatically. </param>
            //public void SetToReferences(BipedReferences references, Transform rootNode = null)
            _headFbbik = fbbik;
            return true;
        }

        // TODO: generalize the 2 fbbik functions into 1
        // this one for when "rotation only" and fixing the hand position
        // only thing that is different is effectors
        public bool CreateHandsFullBodyBipedIK()
        {
            if (!ReadyForIK()) return false;
            var go = InitializeStandardGameObject();
            if (!go) return false;

            // for now: get rid of a potentially existing component
            //FullBodyBipedIK fbbik = go.GetComponent<FullBodyBipedIK>();
            //if (fbbik) UnityEngine.Object.DestroyImmediate(fbbik);

            FullBodyBipedIK fbbik = go.AddComponent<FullBodyBipedIK>();
            IKSolverFullBodyBiped fbbiks = fbbik.solver;

            // set up the internal BipedReferences using avatar animator
            AutoPopulateReferences(ref fbbik.references);
            // set the solver rootNode to the spine?? 
            fbbiks.rootNode = references.spine[0];
            // not 100% weight?
            fbbiks.IKPositionWeight = 1f;

            // set (private) solver root using funny component field method to avatar root(?)
            FieldInfo field = typeof(RootMotion.FinalIK.IKSolverFullBodyBiped).GetField
                ("root", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(fbbiks, (Transform)references.root);

            // === IKSolverFullBody property filling ===

            // default number of solver iterations
            fbbiks.iterations = 4;

            // chain | root node chains (head, left / right arms, left / right legs)
            // the only chain with Child constraints is the head (actually the body / solver root) 
            // pin 0 pull 1 default, setting nodes manually to the fbbik default (all default values except transform)
            FBIKChain headChain = new FBIKChain();
            headChain.pin = 0f; headChain.pull = 1f;
            IKSolver.Node headChainNode = new IKSolver.Node();
            headChainNode.transform = references.head;
            headChain.nodes = new IKSolver.Node[] { headChainNode };

            // children chains are the following 1,2,3,4 chains 
            headChain.children = new int[] { 1, 2, 3, 4 };

            // child chain constraints (not sure why these are how they are but they are) 
            // indexes 1, 2 have 1 on pull elasticity, otherwise 0
            FBIKChain.ChildConstraint[] headCCs = new FBIKChain.ChildConstraint[4];

            // get the bone1/bone2 fields of the ChildConstraint
            FieldInfo bone1 = typeof(RootMotion.FinalIK.FBIKChain.ChildConstraint).GetField
                ("bone1", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo bone2 = typeof(RootMotion.FinalIK.FBIKChain.ChildConstraint).GetField
                ("bone2", BindingFlags.Instance | BindingFlags.NonPublic);

            // FBIKChain.ChildConstraint doesn't have a constructor in stub, but does have a 4 argument instructor in actual
#if ActualFinalIK
            headCCs[0] = new FBIKChain.ChildConstraint(references.leftUpperArm, references.rightThigh, 0f, 1f);
#else
            headCCs[0] = new FBIKChain.ChildConstraint();
#endif
            headCCs[0].pushElasticity = 0f;
            headCCs[0].pullElasticity = 1f;
            bone1.SetValue(headCCs[0], (Transform)references.leftUpperArm);
            bone2.SetValue(headCCs[0], (Transform)references.rightThigh);

#if ActualFinalIK
            headCCs[1] = new FBIKChain.ChildConstraint(references.rightUpperArm, references.leftThigh, 0f, 1f);
#else
            headCCs[1] = new FBIKChain.ChildConstraint();
#endif
            headCCs[1].pushElasticity = 0f;
            headCCs[1].pullElasticity = 1f;
            bone1.SetValue(headCCs[1], (Transform)references.rightUpperArm);
            bone2.SetValue(headCCs[1], (Transform)references.leftThigh);

#if ActualFinalIK
            headCCs[2] = new FBIKChain.ChildConstraint(references.leftUpperArm, references.rightUpperArm, 0f, 0f);
#else
            headCCs[2] = new FBIKChain.ChildConstraint();
#endif
            headCCs[2].pushElasticity = 0f;
            headCCs[2].pullElasticity = 0f;
            bone1.SetValue(headCCs[2], (Transform)references.leftUpperArm);
            bone2.SetValue(headCCs[2], (Transform)references.rightUpperArm);

#if ActualFinalIK
            headCCs[3] = new FBIKChain.ChildConstraint(references.leftThigh, references.rightThigh, 0f, 0f);
#else
            headCCs[3] = new FBIKChain.ChildConstraint();
#endif
            headCCs[3].pushElasticity = 0f;
            headCCs[3].pullElasticity = 0f;
            bone1.SetValue(headCCs[3], (Transform)references.leftThigh);
            bone2.SetValue(headCCs[3], (Transform)references.rightThigh);

            headChain.childConstraints = headCCs;

            FBIKChain leftArmChain = new FBIKChain();
            leftArmChain.pin = 0f; leftArmChain.pull = 1f;
            leftArmChain.nodes = new IKSolver.Node[3];
            leftArmChain.nodes[0] = new IKSolver.Node();
            leftArmChain.nodes[0].transform = references.leftUpperArm;
            leftArmChain.nodes[1] = new IKSolver.Node();
            leftArmChain.nodes[1].transform = references.leftForearm;
            leftArmChain.nodes[2] = new IKSolver.Node();
            leftArmChain.nodes[2].transform = references.leftHand;
            leftArmChain.pull = 1f;
            leftArmChain.reach = 0f;
            leftArmChain.push = 0f;
            leftArmChain.pushParent = 0f;

            FBIKChain rightArmChain = new FBIKChain();
            rightArmChain.pin = 0f; rightArmChain.pull = 1f;
            rightArmChain.nodes = new IKSolver.Node[3];
            rightArmChain.nodes[0] = new IKSolver.Node();
            rightArmChain.nodes[0].transform = references.rightUpperArm;
            rightArmChain.nodes[1] = new IKSolver.Node();
            rightArmChain.nodes[1].transform = references.rightForearm;
            rightArmChain.nodes[2] = new IKSolver.Node();
            rightArmChain.nodes[2].transform = references.rightHand;
            rightArmChain.pull = 1f;
            rightArmChain.reach = 0f;
            rightArmChain.push = 0f;
            rightArmChain.pushParent = 0f;

            FBIKChain leftLegChain = new FBIKChain();
            leftLegChain.pin = 0f; leftLegChain.pull = 1f;
            leftLegChain.nodes = new IKSolver.Node[3];
            leftLegChain.nodes[0] = new IKSolver.Node();
            leftLegChain.nodes[0].transform = references.leftThigh;
            leftLegChain.nodes[1] = new IKSolver.Node();
            leftLegChain.nodes[1].transform = references.leftCalf;
            leftLegChain.nodes[2] = new IKSolver.Node();
            leftLegChain.nodes[2].transform = references.leftFoot;

            FBIKChain rightLegChain = new FBIKChain();
            rightLegChain.pin = 0f; rightLegChain.pull = 1f;
            rightLegChain.nodes = new IKSolver.Node[3];
            rightLegChain.nodes[0] = new IKSolver.Node();
            rightLegChain.nodes[0].transform = references.rightThigh;
            rightLegChain.nodes[1] = new IKSolver.Node();
            rightLegChain.nodes[1].transform = references.rightCalf;
            rightLegChain.nodes[2] = new IKSolver.Node();
            rightLegChain.nodes[2].transform = references.rightFoot;

            fbbiks.chain = new FBIKChain[] { headChain, leftArmChain, rightArmChain, leftLegChain, rightLegChain };

            // effectors | standard fbbik 9 
            // 
            Transform[] effectorsOrder = new Transform[] {
                references.head,
                references.leftUpperArm,
                references.rightUpperArm,
                references.leftThigh,
                references.rightThigh,
                references.leftHand,
                references.rightHand,
                references.leftFoot,
                references.rightFoot
            };

            fbbiks.effectors = new IKEffector[9];

            IKEffector headEffector = new IKEffector();
            headEffector.bone = references.head;
            headEffector.positionWeight = 0f;
            headEffector.rotationWeight = 0f;
            headEffector.maintainRelativePositionWeight = 0f;
            // why? because it's supposed to be body I guess
            headEffector.childBones = new Transform[] { references.leftThigh, references.rightThigh };

            fbbiks.effectors[0] = headEffector;
            for (int i = 1; i < 9; i++)
            {
                fbbiks.effectors[i] = new IKEffector();
                fbbiks.effectors[i].bone = effectorsOrder[i];
                fbbiks.effectors[i].positionWeight = 0f;
                fbbiks.effectors[i].rotationWeight = 0f;
                fbbiks.effectors[i].maintainRelativePositionWeight = 0f;
            }
            // set hand and feet effector plane bones
            // left hand
            fbbiks.effectors[5].planeBone1 = references.leftUpperArm;
            fbbiks.effectors[5].planeBone2 = references.rightUpperArm;
            fbbiks.effectors[5].planeBone3 = references.head;

            fbbiks.effectors[6].planeBone1 = references.rightUpperArm;
            fbbiks.effectors[6].planeBone2 = references.leftUpperArm;
            fbbiks.effectors[6].planeBone3 = references.head;

            fbbiks.effectors[7].planeBone1 = references.leftThigh;
            fbbiks.effectors[7].planeBone2 = references.rightThigh;
            fbbiks.effectors[7].planeBone3 = references.head;

            fbbiks.effectors[8].planeBone1 = references.rightThigh;
            fbbiks.effectors[8].planeBone2 = references.leftThigh;
            fbbiks.effectors[8].planeBone3 = references.head;

            // set hand (not shoulder aka arm) stuff here)
            // ohhh, this is the IK solver VRC is using lol
            // left and right hands
            fbbiks.effectors[5].target = leftHandTarget;
            fbbiks.effectors[5].positionWeight = 1f;
            fbbiks.effectors[5].rotationWeight = 1f;
            fbbiks.effectors[5].maintainRelativePositionWeight = 1f;
            fbbiks.effectors[6].target = rightHandTarget;
            fbbiks.effectors[6].positionWeight = 1f;
            fbbiks.effectors[6].rotationWeight = 1f;
            fbbiks.effectors[6].maintainRelativePositionWeight = 1f;

            // spineMapping
            // first, set Transform[] spineBones and *include* hip (thus +1)
            fbbiks.spineMapping.spineBones = new Transform[references.spine.Length + 1];
            //Array.Copy(references.spine, fbbiks.spineMapping.spineBones, references.spine.Length);
            fbbiks.spineMapping.spineBones[0] = references.pelvis;
            for (int i = 1; i < references.spine.Length + 1; i++)
            {
                fbbiks.spineMapping.spineBones[i] = references.spine[i - 1];
            }

            // set spinemapping for first limb bones off spine
            fbbiks.spineMapping.leftUpperArmBone = references.leftUpperArm;
            fbbiks.spineMapping.rightUpperArmBone = references.rightUpperArm;
            fbbiks.spineMapping.leftThighBone = references.leftThigh;
            fbbiks.spineMapping.rightThighBone = references.rightThigh;

            // boneMappings | head is the only "individual bone" for the solver 
            RootMotion.FinalIK.IKMappingBone headMappingBone = new RootMotion.FinalIK.IKMappingBone(references.head);
            headMappingBone.maintainRotationWeight = 1; // this could maybe be 1 if fbbik is applied after Aim
            fbbiks.boneMappings = new RootMotion.FinalIK.IKMappingBone[] { headMappingBone };

            // limbMapping | standard biped stuff
            // we don't actually want any of these IK limbs to affect the avatar (for now?) so weights = 0

            // note weight 1 for weight and rotationweight for arms here
            IKMappingLimb leftArmLimb = new RootMotion.FinalIK.IKMappingLimb();
            leftArmLimb.maintainRotationWeight = 0f;
            leftArmLimb.weight = 1;
            leftArmLimb.parentBone = references.leftUpperArm.parent; // this is fine
            leftArmLimb.bone1 = references.leftUpperArm;
            leftArmLimb.bone2 = references.leftForearm;
            leftArmLimb.bone3 = references.leftHand;
            IKMappingLimb rightArmLimb = new RootMotion.FinalIK.IKMappingLimb();
            rightArmLimb.maintainRotationWeight = 0f;
            rightArmLimb.weight = 1;
            rightArmLimb.parentBone = references.rightUpperArm.parent; // this is fine
            rightArmLimb.bone1 = references.rightUpperArm;
            rightArmLimb.bone2 = references.rightForearm;
            rightArmLimb.bone3 = references.rightHand;

            // weight and maintainRotationWeight were 1 for the leg limbs on the reference gote.. so...
            IKMappingLimb leftLegLimb = new RootMotion.FinalIK.IKMappingLimb();
            leftLegLimb.maintainRotationWeight = 1f;
            leftLegLimb.weight = 1;
            //leftLegLimb.parentBone = // leave parent bone for legs as null
            leftLegLimb.bone1 = references.leftThigh;
            leftLegLimb.bone2 = references.leftCalf;
            leftLegLimb.bone3 = references.leftFoot;
            IKMappingLimb rightLegLimb = new RootMotion.FinalIK.IKMappingLimb();
            rightLegLimb.maintainRotationWeight = 1f;
            rightLegLimb.weight = 1;
            //leftLegLimb.parentBone = // leave parent bone for legs as null
            rightLegLimb.bone1 = references.rightThigh;
            rightLegLimb.bone2 = references.rightCalf;
            rightLegLimb.bone3 = references.rightFoot;

            fbbiks.limbMappings = new RootMotion.FinalIK.IKMappingLimb[] { leftArmLimb, rightArmLimb, leftLegLimb, rightLegLimb };

            // FABRIK pass | don't actually care as we don't have effectors on the limbs, but leave default.. 
            fbbiks.FABRIKPass = true; // shouldn't have a difference if false..

            // some arbitrary values
            fbbiks.spineStiffness = 0.35f;
            //fbbiks.pullBodyHorizontal = 0.1f;

            // component disabled if using IKExecutionOrder
            fbbik.enabled = false;

            _handsFbbik = fbbik;
            return true;
        }

        // Limb IKs instead for the 2 arms? 
        // you can't use ArmIK and using VRIK for this seems crazy (and bad and annoying... very annoying...)
        public bool CreateHandsLimbIKs()
        {
            if (!ReadyForIK()) return false;
            var go = InitializeStandardGameObject();
            if (!go) return false;

            _leftHandlik = go.AddComponent<LimbIK>();
            _leftHandlik.fixTransforms = true;
            IKSolverLimb lliks = _leftHandlik.solver;

            lliks.goal = AvatarIKGoal.LeftHand;
            // manual bend goal for matching to animated position
            lliks.bendModifier = IKSolverLimb.BendModifier.Goal;
            lliks.bendGoal = leftArmBendTarget;
            // follow strongly
            lliks.bendModifierWeight = 1f;
            // want the hand to stay as same as possible to the original animation, but since this is after aimIK, we don't want to include that rotation
            lliks.maintainRotationWeight = 0f;
            // set (private) solver root using funny component field method to avatar root(?)
            FieldInfo field = typeof(RootMotion.FinalIK.IKSolverLimb).GetField
                ("root", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(lliks, (Transform)references.root);
            // dunno lol
            lliks.IKPosition = references.root.position;
            lliks.target = leftHandTarget;

            // use shoulder for first bone for limb IK magic and prevent weird arm rotations!!!
            // exception if there is no shoulder :shrug: 
            Transform bone1Source = references.leftUpperArm.parent;
            if (bone1Source == animator.GetBoneTransform(HumanBodyBones.Spine) ||
                bone1Source == animator.GetBoneTransform(HumanBodyBones.Chest) ||
                bone1Source == animator.GetBoneTransform(HumanBodyBones.UpperChest)) {
                bone1Source = references.leftUpperArm; 
            }
            lliks.bone1 = new IKSolverTrigonometric.TrigonometricBone();
            lliks.bone1.transform = bone1Source;
            lliks.bone1.weight = 0f;
            lliks.bone1.defaultLocalPosition = bone1Source.localPosition;
            lliks.bone1.defaultLocalRotation = bone1Source.localRotation;

            lliks.bone2 = new IKSolverTrigonometric.TrigonometricBone();
            lliks.bone2.transform = references.leftForearm;
            lliks.bone2.weight = 1f;
            lliks.bone2.defaultLocalPosition = references.leftForearm.localPosition;
            lliks.bone2.defaultLocalRotation = references.leftForearm.localRotation;

            lliks.bone3 = new IKSolverTrigonometric.TrigonometricBone();
            lliks.bone3.transform = references.leftHand;
            lliks.bone3.weight = 1f;
            lliks.bone3.defaultLocalPosition = references.leftHand.localPosition;
            lliks.bone3.defaultLocalRotation = references.leftHand.localRotation;

            _rightHandlik = go.AddComponent<LimbIK>();
            _rightHandlik.fixTransforms = true;
            IKSolverLimb rliks = _rightHandlik.solver;

            rliks.target = rightHandTarget;
            rliks.goal = AvatarIKGoal.RightHand;
            // manual bend goal for matching to animated position
            rliks.bendModifier = IKSolverLimb.BendModifier.Goal;
            rliks.bendGoal = rightArmBendTarget;
            // follow strongly
            rliks.bendModifierWeight = 1f;
            // want the hand to stay as same as possible to the original animation, but since this is after aimIK, we don't want to include that rotation
            rliks.maintainRotationWeight = 0f;
            field.SetValue(rliks, (Transform)references.root);
            // dunno lol
            rliks.IKPosition = references.root.position;

            // use shoulder for first bone for limb IK magic and prevent weird arm rotations!!!
            // exception if there is no shoulder :shrug: 
            bone1Source = references.rightUpperArm.parent;
            if (bone1Source == animator.GetBoneTransform(HumanBodyBones.Spine) ||
                bone1Source == animator.GetBoneTransform(HumanBodyBones.Chest) ||
                bone1Source == animator.GetBoneTransform(HumanBodyBones.UpperChest))
            {
                bone1Source = references.rightUpperArm;
            }
            rliks.bone1 = new IKSolverTrigonometric.TrigonometricBone();
            rliks.bone1.transform = bone1Source;
            rliks.bone1.weight = 0f;
            rliks.bone1.defaultLocalPosition = bone1Source.localPosition;
            rliks.bone1.defaultLocalRotation = bone1Source.localRotation;

            rliks.bone2 = new IKSolverTrigonometric.TrigonometricBone();
            rliks.bone2.transform = references.rightForearm;
            rliks.bone2.weight = 1f;
            rliks.bone2.defaultLocalPosition = references.rightForearm.localPosition;
            rliks.bone2.defaultLocalRotation = references.rightForearm.localRotation;

            rliks.bone3 = new IKSolverTrigonometric.TrigonometricBone();
            rliks.bone3.transform = references.rightHand;
            rliks.bone3.weight = 1f;
            rliks.bone3.defaultLocalPosition = references.rightHand.localPosition;
            rliks.bone3.defaultLocalRotation = references.rightHand.localRotation;

            _leftHandlik.enabled = false;
            _rightHandlik.enabled = false;

            return true;
        }

       
        public bool CreateStandardIKExecutionOrders()
        {
            var go = InitializeStandardGameObject();
            if (!go) return false;

            // create 2 sub objects under the Standard IK object
            Transform both = new GameObject("Both").transform;
            both.parent = go.transform;
            both.position = go.transform.position;
            both.rotation = go.transform.rotation;

            Transform rot = new GameObject("Rotation Only").transform;
            rot.parent = go.transform;
            rot.position = go.transform.position;
            rot.rotation = go.transform.rotation;

            Transform rotp = new GameObject("Rotation Plus").transform;
            rotp.parent = go.transform;
            rotp.position = go.transform.position;
            rotp.rotation = go.transform.rotation;

            Transform rotub = new GameObject("Rotation UpperBody").transform;
            rotub.parent = go.transform;
            rotub.position = go.transform.position;
            rotub.rotation = go.transform.rotation;

            //AimIK aik = go.GetComponent<AimIK>();
            //FullBodyBipedIK fbbik = go.GetComponent<FullBodyBipedIK>();

            //if (aik == null || fbbik == null) return false;
            //if (_aik == null || _headFbbik == null || _handsFbbik == null) return false;
            if (_aik == null || _aik_head == null || _aik_upperbody == null || 
                _headFbbik == null || _leftHandlik == null || _rightHandlik == null) return false;

            var StandardIKEOBoth = both.gameObject.AddComponent<RootMotion.FinalIK.IKExecutionOrder>();
            // fbbik first, then aim?
            StandardIKEOBoth.IKComponents = new RootMotion.FinalIK.IK[] { _headFbbik, _aik_head };
            StandardIKEOBoth.enabled = false; 
            // IKExecutionOrder.animator is option for when Animate Physics

            var StandardIKEORot = rot.gameObject.AddComponent<RootMotion.FinalIK.IKExecutionOrder>();
            // aim only
            StandardIKEORot.IKComponents = new RootMotion.FinalIK.IK[] { _aik };
            StandardIKEORot.enabled = false;
            // IKExecutionOrder.animator is option for when Animate Physics

            var StandardIKEORotub = rotub.gameObject.AddComponent<RootMotion.FinalIK.IKExecutionOrder>();
            // aim only
            StandardIKEORotub.IKComponents = new RootMotion.FinalIK.IK[] { _aik_upperbody };
            StandardIKEORotub.enabled = false;
            // IKExecutionOrder.animator is option for when Animate Physics

            var StandardIKEORotp = rotp.gameObject.AddComponent<RootMotion.FinalIK.IKExecutionOrder>();
            //// hand position fix applied after aim rotation, unlike movement first then aim in "both" 
            //StandardIKEORotp.IKComponents = new RootMotion.FinalIK.IK[] { _aik, _handsFbbik };
            StandardIKEORotp.IKComponents = new RootMotion.FinalIK.IK[] { _aik, _leftHandlik, _rightHandlik };
            StandardIKEORotp.enabled = false;
            // IKExecutionOrder.animator is option for when Animate Physics
            return true;
        }

        public bool AddVRCPrefab(UnityEngine.Object pf)
        {
            if (deskyModeRoot == null && !InitializeGimmickRoot()) return false;
            var pfInstance = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            pfInstance.name = pf.name;

            // dumb name search method 
            Transform got = animator.transform.Find(pf.name);
            if (got)
            {
                // for now, delete supposedly old version
                UnityEngine.Object.DestroyImmediate(got.gameObject);
                Console.WriteLine("Destoyed old DeskyMode prefab object");
            }

            pfInstance.transform.parent = deskyModeRoot;
            pfInstance.transform.position = deskyModeRoot.position;
            pfInstance.transform.rotation = deskyModeRoot.rotation;
            return true;
        }
    }
}
