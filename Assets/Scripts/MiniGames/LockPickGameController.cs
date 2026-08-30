using System;
using Escape.SceneFlow;
using Escape.Localization;
using Escape.Progress;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Audio;
using Escape.Rooms;
using Escape.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Escape.MiniGames
{
    // 씬에 배치된 3D 물리 오브젝트로 잠금따기 핀과 픽을 제어한다.
    [MovedFrom(true, "Escape.MiniGames", null, "LockPickGameManager")]
    public sealed class LockPickGameController : MonoBehaviour
    {
        private const string TextAssetPath = "Assets/Resources/Data/text.tsv";
        private const string DefaultSuccessTid = "lockpick_success";
        private const string EntryStartTid = "lockpick_entry_start";
        private const int DrawerPinCount = 3;
        private const int UtilityDoorPinCount = 4;
        private const int EntrancePadlockPinCount = 5;
        private const int HandcuffsPinCount = 2;
        private const float SharedRequiredLiftRatio = 0.5f;
        private const float HandcuffsTutorialLiftRatio = 0.25f;
        private const float HandcuffsTutorialMaximumLengthRatio = 0.75f;
        private const float HandcuffsTutorialFirstPinLengthRatio = 0.55f;
        private const float HandcuffsTutorialSecondPinLengthRatio = 0.65f;
        private const float MinimumLowerPinLengthRatio = 0.9f;

        [Header("Lock")]
        [SerializeField, Min(0)] private int lockSeed;
        [SerializeField, Range(0.05f, 0.25f)] private float setTolerance = 0.12f;
        [SerializeField, Min(0.05f)] private float pinTravel = 1.22f;
        [SerializeField, Range(0.1f, 1f)] private float requiredLiftMin = 0.28f;
        [SerializeField, Range(0.1f, 1f)] private float requiredLiftMax = 0.62f;
        [SerializeField, Min(0f)] private float contactRadius = 0.34f;

        [Header("Pick")]
        [SerializeField] private Camera inputCamera;
        [SerializeField] private Transform pickRoot;
        [SerializeField] private Transform pickTip;
        [SerializeField] private Rigidbody2D pickRigidbody;
        [SerializeField] private TargetJoint2D pickHandleJoint;
        [SerializeField] private Collider2D[] pickColliders = Array.Empty<Collider2D>();
        [SerializeField, Min(0.1f)] private float pickGripFrequency = 6.5f;
        [SerializeField, Range(0f, 1f)] private float pickGripDampingRatio = 0.78f;
        [SerializeField, Min(0.1f)] private float pickGripMaxForce = 10f;
        [SerializeField, Min(0.1f)] private float pickReturnMaxForce = 3.5f;
        [SerializeField, Min(0f)] private float pickLinearDamping = 7f;
        [SerializeField, Min(0f)] private float pickAngularDamping = 12f;
        [Header("Pins")]
        [SerializeField] private PinBody[] pins = Array.Empty<PinBody>();
        [SerializeField, Min(0f)] private float pinOutwardGravityScale = 0.08f;
        [SerializeField, Min(0.01f)] private float pinMass = 0.55f;
        [SerializeField, Min(0f)] private float pinLinearDamping = 6f;
        [SerializeField, Min(0f)] private float pinAngularDamping = 0f;
        [SerializeField, Min(0.01f)] private float pinSpringFrequency = 0.65f;
        [SerializeField, Range(0f, 1f)] private float pinSpringDampingRatio = 1f;
        [SerializeField, Min(0f)] private float pinSetSnapDistance = 0.018f;
        [SerializeField, Min(0f)] private float pinSetSettleSpeed = 0.14f;
        [SerializeField, Min(0f)] private float pinSetLockThreshold = 0.0025f;
        [SerializeField, Min(0f)] private float inactivePinLiftLimit = 0.025f;
        [SerializeField, Range(0.5f, 3f)] private float lowerPinLengthVariationScale = 2f;
        [SerializeField] private Collider2D[] pinStopColliders = Array.Empty<Collider2D>();
        [SerializeField, Min(0f)] private float pinStopSkin = 0.002f;

        [Header("Pin Colors")]
        [SerializeField] private Color setLowerPinColor = new(0.45f, 0.84f, 0.92f, 1f);
        [SerializeField] private Color setUpperPinColor = new(0.30f, 0.70f, 0.78f, 1f);

        [Header("Transition")]
        [SerializeField, Min(0.01f)] private float exitTransitionSeconds = 0.55f;

        [Header("UI")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button exitButton;

        [Header("Success")]
        [SerializeField] private TMP_Text successText;
        [SerializeField] private CanvasGroup successCanvasGroup;
        [SerializeField, TsvId(TextAssetPath)] private string successTid = DefaultSuccessTid;
        [SerializeField] private string successMessage = "해제 성공!";
        [SerializeField, Min(0.05f)] private float successRevealSeconds = 0.48f;
        [SerializeField, Min(0f)] private float successHoldSeconds = 0.55f;
        [SerializeField, Min(0.01f)] private float successPinSweepStepSeconds = 0.08f;
        [SerializeField] private Color successPinSweepLowerColor = new(0.72f, 0.96f, 1f, 1f);
        [SerializeField] private Color successPinSweepUpperColor = new(0.48f, 0.86f, 0.92f, 1f);

        private const float PickReturnSpeed = 6f;
        private const float PickReturnRotationSpeed = 720f;
        private const float PickReturnSnapDistance = 0.0025f;
        private const float PickReturnMaxSeconds = 0.7f;
        private const float PinSetClickDistance = 0.003f;
        private const int PinBottomShapeCount = 1;
        private const int PinBottomShapeTextureSize = 96;
        private const float PinBottomShapeHeightRatio = 0.32f;
        private const float PinBottomShapeOverlap = 0.002f;
        private int[] bindingOrder = Array.Empty<int>();
        private int[] pinOrderIndices = Array.Empty<int>();
        private int[] setSequence = Array.Empty<int>();
        private float[] requiredLifts = Array.Empty<float>();
        private float[] pinLengthRatios = Array.Empty<float>();
        private float[] currentLifts = Array.Empty<float>();
        private PinBottomShape[] lowerPinBottomShapes = Array.Empty<PinBottomShape>();
        private Vector3[] pinBasePositions = Array.Empty<Vector3>();
        private Vector3[] upperPinBasePositions = Array.Empty<Vector3>();
        private Vector3[] lowerPinBasePositions = Array.Empty<Vector3>();
        private Vector3[] upperPinBaseScales = Array.Empty<Vector3>();
        private Vector3[] lowerPinBaseScales = Array.Empty<Vector3>();
        private Vector3[] springBasePositions = Array.Empty<Vector3>();
        private Vector3[] springBaseScales = Array.Empty<Vector3>();
        private float[] lowerPinBottomShapeHeights = Array.Empty<float>();
        private float[] pinMaximumPositions = Array.Empty<float>();
        private Color[] upperPinDefaultColors = Array.Empty<Color>();
        private Color[] lowerPinDefaultColors = Array.Empty<Color>();
        private Sprite[] lowerPinDefaultSprites = Array.Empty<Sprite>();
        private bool[] pinSet = Array.Empty<bool>();
        private bool[] releaseBlockedPins = Array.Empty<bool>();
        private Vector3 pickRestPosition;
        private Plane dragPlane;
        private int sequenceIndex;
        private int activeContactPin = -1;
        private bool draggingPick;
        private bool returningPick;
        private bool solved;
        private bool exitingToRoom;
        private bool entrySplashComplete;
        private bool showingSuccess;
        private bool successPinSweepActive;
        private bool isHandcuffsTutorial;
        private int successPinSweepIndex = -1;
        private float pickRestRotation;
        private float pickReturnElapsed;
        private Vector2 pickGripRestPosition;
        private Vector3 pickDragOffset;
        private Sprite[] lowerPinBottomShapeSprites = Array.Empty<Sprite>();
        private SpriteRenderer[][] lowerPinBottomPresetRenderers = Array.Empty<SpriteRenderer[]>();

        private enum PinBottomShape
        {
            ConvexTriangle
        }

        private void Awake()
        {
            if (!ConfigurePinsForTarget() || !HasRequiredReferences())
            {
                enabled = false;
                return;
            }

            inputCamera ??= Camera.main;
            ConfigureRigidbodies();
            BindButtons();
            ConfigureExitButtonAvailability();
            CaptureDefaults();
            BuildLockFromSeed();
            RestartGame();
            SetGameplayInteractable(false);
        }

        // 진입 대상에 맞춰 핀 수를 줄이고 수갑은 쉬운 튜토리얼 규칙을 적용한다.
        private bool ConfigurePinsForTarget()
        {
            LockPickUnlockTarget unlockTarget = SceneLoadArgs.PendingLockPickUnlockTarget;
            isHandcuffsTutorial = unlockTarget == LockPickUnlockTarget.Handcuffs;
            int requiredPinCount = unlockTarget switch
            {
                LockPickUnlockTarget.Handcuffs => HandcuffsPinCount,
                LockPickUnlockTarget.UtilityDoor => UtilityDoorPinCount,
                LockPickUnlockTarget.EntrancePadlock => EntrancePadlockPinCount,
                _ => DrawerPinCount,
            };

            if (pins == null || pins.Length < requiredPinCount)
            {
                Debug.LogError(
                    $"[LockPick] Target requires {requiredPinCount} pins, but only {pins?.Length ?? 0} are assigned.",
                    this);
                return false;
            }

            CenterActivePins(requiredPinCount);
            for (int i = 0; i < pins.Length; i++)
            {
                Transform root = pins[i]?.Root;
                if (root == null)
                {
                    continue;
                }

                GameObject pinGroup = root.parent != null ? root.parent.gameObject : root.gameObject;
                pinGroup.SetActive(i < requiredPinCount);
            }

            if (pins.Length != requiredPinCount)
            {
                Array.Resize(ref pins, requiredPinCount);
            }

            return true;
        }

        // 5핀 배치의 중심과 간격을 기준으로 적은 수의 핀도 좌우 여백이 같게 맞춘다.
        private void CenterActivePins(int activePinCount)
        {
            if (activePinCount >= pins.Length || pins.Length < 2)
            {
                return;
            }

            Transform firstRoot = pins[0]?.Root;
            Transform lastRoot = pins[^1]?.Root;
            if (firstRoot == null || lastRoot == null)
            {
                return;
            }

            float centerX = (firstRoot.position.x + lastRoot.position.x) * 0.5f;
            float spacing = (lastRoot.position.x - firstRoot.position.x) / (pins.Length - 1);
            float firstTargetX = centerX - spacing * (activePinCount - 1) * 0.5f;
            for (int i = 0; i < activePinCount; i++)
            {
                Transform root = pins[i]?.Root;
                if (root == null)
                {
                    continue;
                }

                Transform pinGroup = root.parent != null ? root.parent : root;
                float targetX = firstTargetX + spacing * i;
                pinGroup.position += Vector3.right * (targetX - root.position.x);
            }
        }

        private void Start()
        {
            SoundPlayer.PlayBgm(ChipSongId.LockPickPuzzle);
            PlayEntryStartFlow(destroyCancellationToken).Forget();
        }

        private void OnDestroy()
        {
            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(OnExitButtonClicked);
            }

            if (retryButton != null)
            {
                retryButton.onClick.RemoveListener(OnRetryButtonClicked);
            }
        }

        private void Update()
        {
            UpdatePointerInput();
            if (returningPick)
            {
                UpdatePickReturn();
            }

            UpdatePinState();
            UpdatePinVisuals();
        }

        public void ConfigureSceneReferences(
            Camera cameraReference,
            Transform pickRootReference,
            Transform pickTipReference,
            Rigidbody2D pickRigidbodyReference,
            Collider2D[] pickColliderReferences,
            PinBody[] pinReferences,
            TargetJoint2D pickHandleJointReference = null)
        {
            inputCamera = cameraReference;
            pickRoot = pickRootReference;
            pickTip = pickTipReference;
            pickRigidbody = pickRigidbodyReference;
            if (pickHandleJointReference != null)
            {
                pickHandleJoint = pickHandleJointReference;
            }

            pickColliders = pickColliderReferences ?? Array.Empty<Collider2D>();
            pins = pinReferences ?? Array.Empty<PinBody>();
        }

        private bool HasRequiredReferences()
        {
            if (pickRoot == null ||
                pickTip == null ||
                pickRigidbody == null ||
                pickHandleJoint == null ||
                pins == null ||
                pins.Length == 0)
            {
                Debug.LogError("[LockPick] Physics scene references are missing.", this);
                return false;
            }

            for (int i = 0; i < pins.Length; i++)
            {
                if (!pins[i].HasRequiredReferences)
                {
                    Debug.LogError($"[LockPick] Pin physics reference is missing: {i}", this);
                    return false;
                }
            }

            return true;
        }

        private void ConfigureRigidbodies()
        {
            pickRigidbody.bodyType = RigidbodyType2D.Dynamic;
            pickRigidbody.gravityScale = 0f;
            pickRigidbody.linearDamping = pickLinearDamping;
            pickRigidbody.angularDamping = pickAngularDamping;
            pickRigidbody.constraints = RigidbodyConstraints2D.None;
            pickRigidbody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            pickRigidbody.interpolation = RigidbodyInterpolation2D.Interpolate;

            pickHandleJoint.autoConfigureTarget = false;
            pickHandleJoint.anchor = Vector2.zero;
            pickHandleJoint.dampingRatio = pickGripDampingRatio;
            pickHandleJoint.frequency = pickGripFrequency;
            pickHandleJoint.maxForce = pickReturnMaxForce;
            pickHandleJoint.enabled = false;

            for (int i = 0; i < pins.Length; i++)
            {
                Rigidbody2D body = pins[i].Body;
                body.bodyType = RigidbodyType2D.Dynamic;
                body.mass = pinMass;
                body.gravityScale = pinOutwardGravityScale;
                body.linearDamping = pinLinearDamping;
                body.angularDamping = pinAngularDamping;
                body.constraints = RigidbodyConstraints2D.FreezePositionX |
                    RigidbodyConstraints2D.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;

                SpringJoint2D joint = pins[i].SpringJoint;
                joint.autoConfigureDistance = false;
                joint.enableCollision = false;
                joint.frequency = pinSpringFrequency;
                joint.dampingRatio = pinSpringDampingRatio;
            }
        }

        private void CaptureDefaults()
        {
            int count = pins.Length;
            currentLifts = new float[count];
            pinSet = new bool[count];
            pinBasePositions = new Vector3[count];
            upperPinBasePositions = new Vector3[count];
            lowerPinBasePositions = new Vector3[count];
            upperPinBaseScales = new Vector3[count];
            lowerPinBaseScales = new Vector3[count];
            springBasePositions = new Vector3[count];
            springBaseScales = new Vector3[count];
            lowerPinBottomShapeHeights = new float[count];
            pinMaximumPositions = new float[count];
            upperPinDefaultColors = new Color[count];
            lowerPinDefaultColors = new Color[count];
            lowerPinDefaultSprites = new Sprite[count];
            lowerPinBottomPresetRenderers = new SpriteRenderer[count][];

            for (int i = 0; i < count; i++)
            {
                pinBasePositions[i] = pins[i].Root.position;
                upperPinBasePositions[i] = pins[i].UpperPin.localPosition;
                lowerPinBasePositions[i] = pins[i].LowerPin.localPosition;
                upperPinBaseScales[i] = pins[i].UpperPin.localScale;
                lowerPinBaseScales[i] = pins[i].LowerPin.localScale;
                ExpandLowerPinBaseFromBottomShape(i);
                springBasePositions[i] = pins[i].SpringVisual.position;
                springBaseScales[i] = pins[i].SpringVisual.localScale;
                upperPinDefaultColors[i] = GetRendererColor(pins[i].UpperPinRenderer);
                lowerPinDefaultColors[i] = GetRendererColor(pins[i].LowerPinRenderer);
                lowerPinDefaultSprites[i] = pins[i].LowerPinRenderer is SpriteRenderer lowerRenderer
                    ? lowerRenderer.sprite
                    : null;
            }

            dragPlane = new Plane(Vector3.forward, pickRoot.position);
            pickRestPosition = pickRoot.position;
            pickRestRotation = pickRigidbody.rotation;
            pickGripRestPosition = pickRoot.position;
        }

        private void ExpandLowerPinBaseFromBottomShape(int pinIndex)
        {
            SpriteRenderer bottomRenderer = pins[pinIndex].LowerPinBottomRenderer;
            if (bottomRenderer == null)
            {
                return;
            }

            float capHeight = Mathf.Abs(bottomRenderer.transform.localScale.y);
            if (capHeight <= 0f)
            {
                return;
            }

            Vector3 scale = lowerPinBaseScales[pinIndex];
            Vector3 position = lowerPinBasePositions[pinIndex];
            float bodyHeight = Mathf.Abs(scale.y);
            float topY = position.y + bodyHeight * 0.5f;
            float fullHeight = bodyHeight + capHeight;
            scale.y = Mathf.Sign(scale.y == 0f ? 1f : scale.y) * fullHeight;
            position.y = topY - fullHeight * 0.5f;
            lowerPinBaseScales[pinIndex] = scale;
            lowerPinBasePositions[pinIndex] = position;
        }

        private void BuildLockFromSeed()
        {
            int count = Mathf.Max(1, pins.Length);
            bindingOrder = new int[count];
            pinOrderIndices = new int[count];
            setSequence = new int[count];
            requiredLifts = new float[count];
            pinLengthRatios = new float[count];
            releaseBlockedPins = new bool[count];
            lowerPinBottomShapes = new PinBottomShape[count];
            int seed = lockSeed != 0 ? lockSeed : GameSession.Instance?.RunSeed ?? 1;
            var random = new System.Random(seed);
            float liftMin = Mathf.Min(requiredLiftMin, requiredLiftMax);
            float liftMax = Mathf.Max(requiredLiftMin, requiredLiftMax);

            for (int i = 0; i < count; i++)
            {
                bindingOrder[i] = i;
                float requiredLiftRatio = isHandcuffsTutorial
                    ? HandcuffsTutorialLiftRatio
                    : SharedRequiredLiftRatio;
                requiredLifts[i] = Mathf.Lerp(liftMin, liftMax, requiredLiftRatio);
                pinLengthRatios[i] = isHandcuffsTutorial
                    ? requiredLifts[i]
                    : Mathf.Lerp(liftMin, liftMax, (i + (float)random.NextDouble()) / count);
            }

            // 일반 자물쇠는 높이 구간마다 하나씩 뽑은 뒤 위치를 섞어 길이 차이가 고르게 보이게 한다.
            if (!isHandcuffsTutorial)
            {
                for (int i = count - 1; i > 0; i--)
                {
                    int swapIndex = random.Next(i + 1);
                    (pinLengthRatios[i], pinLengthRatios[swapIndex]) =
                        (pinLengthRatios[swapIndex], pinLengthRatios[i]);
                }

                // 수갑 튜토리얼은 왼쪽부터 1, 2 순서로 익히고, 다른 자물쇠만 시드 순서를 사용한다.
                for (int i = count - 1; i > 0; i--)
                {
                    int swapIndex = random.Next(i + 1);
                    (bindingOrder[i], bindingOrder[swapIndex]) = (bindingOrder[swapIndex], bindingOrder[i]);
                }
            }

            for (int orderIndex = 0; orderIndex < bindingOrder.Length; orderIndex++)
            {
                pinOrderIndices[bindingOrder[orderIndex]] = orderIndex;
            }

            ClearSetSequence();
            AssignLowerPinBottomShapes();
            AlignPinBasePositionsToSharedTop();
        }

        private void AssignLowerPinBottomShapes()
        {
            if (lowerPinBottomShapes.Length != pins.Length)
            {
                lowerPinBottomShapes = new PinBottomShape[pins.Length];
            }

            for (int i = 0; i < lowerPinBottomShapes.Length; i++)
            {
                lowerPinBottomShapes[i] = PinBottomShape.ConvexTriangle;
            }
        }

        // 서로 다른 핀 길이는 아래쪽으로만 드러나도록 모든 핀의 초기 상단점을 같은 높이에 맞춘다.
        private void AlignPinBasePositionsToSharedTop()
        {
            int count = Mathf.Min(
                pins.Length,
                pinBasePositions.Length,
                requiredLifts.Length,
                pinLengthRatios.Length);
            if (count == 0)
            {
                return;
            }

            float sharedTopY = 0f;
            for (int i = 0; i < count; i++)
            {
                sharedTopY += pinBasePositions[i].y + upperPinBasePositions[i].y +
                    Mathf.Abs(upperPinBaseScales[i].y) * 0.5f;
            }

            sharedTopY /= count;
            for (int i = 0; i < count; i++)
            {
                Vector3 basePosition = pinBasePositions[i];
                basePosition.y = sharedTopY - upperPinBasePositions[i].y -
                    Mathf.Abs(upperPinBaseScales[i].y) * 0.5f;
                pinBasePositions[i] = basePosition;

                SpringJoint2D joint = pins[i].SpringJoint;
                joint.connectedAnchor = basePosition;
            }
        }

        private void RestartGame()
        {
            solved = false;
            draggingPick = false;
            returningPick = false;
            activeContactPin = -1;
            sequenceIndex = 0;
            Array.Clear(pinSet, 0, pinSet.Length);
            Array.Clear(releaseBlockedPins, 0, releaseBlockedPins.Length);
            ClearSetSequence();
            AlignPinBasePositionsToSharedTop();
            ApplyBottomPinLengthVariation();
            ApplyLowerPinBottomShapes();
            RecalculatePinMaximumPositions();
            ResetPins();
            ResetPickPosition();
            HideSuccessFeedback();
            if (retryButton != null)
            {
                retryButton.interactable = true;
            }
        }

        // 시작 시 핀 몸체의 시각 길이를 시드 기반으로 조금씩 다르게 만든다.
        private void ApplyBottomPinLengthVariation()
        {
            int count = Mathf.Min(
                pins.Length,
                pinBasePositions.Length,
                requiredLifts.Length,
                pinLengthRatios.Length);
            if (count == 0)
            {
                return;
            }

            float[] seamPositions = new float[count];
            float[] lowerLengths = new float[count];
            float averageLowerLength = 0f;
            float averageLengthRatio = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 upperPosition = upperPinBasePositions[i];
                Vector3 lowerPosition = lowerPinBasePositions[i];
                float upperLength = upperPinBaseScales[i].y;
                float lowerLength = lowerPinBaseScales[i].y;
                float seamY = ((upperPosition.y - upperLength * 0.5f) +
                    (lowerPosition.y + lowerLength * 0.5f)) * 0.5f;

                seamPositions[i] = seamY;
                averageLowerLength += lowerLength;
                averageLengthRatio += pinLengthRatios[i];
            }

            averageLowerLength /= count;
            averageLengthRatio /= count;
            for (int i = 0; i < count; i++)
            {
                lowerLengths[i] = Mathf.Max(
                    0.05f,
                    averageLowerLength + (averageLengthRatio - pinLengthRatios[i]) * pinTravel);
            }
            float effectiveLengthVariationScale = lowerPinLengthVariationScale;
            if (!isHandcuffsTutorial)
            {
                float shortestLowerLength = lowerLengths[0];
                for (int i = 1; i < count; i++)
                {
                    shortestLowerLength = Mathf.Min(shortestLowerLength, lowerLengths[i]);
                }

                float shortestDeviation = averageLowerLength - shortestLowerLength;
                if (shortestDeviation > Mathf.Epsilon)
                {
                    float minimumLowerLength = averageLowerLength * MinimumLowerPinLengthRatio;
                    float maximumSafeVariationScale =
                        (averageLowerLength - minimumLowerLength) / shortestDeviation;
                    effectiveLengthVariationScale =
                        Mathf.Min(lowerPinLengthVariationScale, maximumSafeVariationScale);
                }
            }

            float handcuffsTutorialLongestVariation = 0f;
            if (isHandcuffsTutorial)
            {
                float liftRange = Mathf.Abs(requiredLiftMax - requiredLiftMin);
                handcuffsTutorialLongestVariation =
                    liftRange * pinTravel * lowerPinLengthVariationScale * 0.5f;
            }

            for (int i = 0; i < count; i++)
            {
                Vector3 upperScale = upperPinBaseScales[i];
                Vector3 lowerScale = lowerPinBaseScales[i];
                Vector3 upperPosition = upperPinBasePositions[i];
                Vector3 lowerPosition = lowerPinBasePositions[i];
                float seamY = seamPositions[i];

                lowerScale.y = Mathf.Max(0.05f, isHandcuffsTutorial
                    ? averageLowerLength + handcuffsTutorialLongestVariation *
                        Mathf.Min(
                            i == 0
                                ? HandcuffsTutorialFirstPinLengthRatio
                                : HandcuffsTutorialSecondPinLengthRatio,
                            HandcuffsTutorialMaximumLengthRatio)
                    : averageLowerLength +
                        (lowerLengths[i] - averageLowerLength) * effectiveLengthVariationScale);
                upperPosition.y = seamY + upperScale.y * 0.5f;
                lowerPosition.y = seamY - lowerScale.y * 0.5f;

                pins[i].UpperPin.localScale = upperScale;
                pins[i].LowerPin.localScale = lowerScale;
                pins[i].UpperPin.localPosition = upperPosition;
                pins[i].LowerPin.localPosition = lowerPosition;
            }
        }

        private void ResetPins()
        {
            for (int i = 0; i < pins.Length; i++)
            {
                Rigidbody2D body = pins[i].Body;
                body.bodyType = RigidbodyType2D.Dynamic;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.position = pinBasePositions[i];
                body.rotation = 0f;
                body.Sleep();
            }
        }

        private void ResetPickPosition()
        {
            ReturnPickToRest();
        }

        private void ReturnPickToRest()
        {
            returningPick = false;
            pickReturnElapsed = 0f;
            SetPickCollisionEnabled(false);
            pickRigidbody.bodyType = RigidbodyType2D.Kinematic;
            pickRigidbody.position = pickRestPosition;
            pickRigidbody.rotation = pickRestRotation;
            pickRoot.SetPositionAndRotation(
                pickRestPosition,
                Quaternion.Euler(0f, 0f, pickRestRotation));
            Physics2D.SyncTransforms();
            pickRigidbody.linearVelocity = Vector2.zero;
            pickRigidbody.angularVelocity = 0f;
            SetPickJointTarget(pickGripRestPosition, pickReturnMaxForce);
            pickHandleJoint.enabled = false;
            pickRigidbody.Sleep();
        }

        private void StartPickReturnToRest()
        {
            returningPick = true;
            pickReturnElapsed = 0f;
            SetPickCollisionEnabled(false);
            pickHandleJoint.enabled = false;
            pickRigidbody.bodyType = RigidbodyType2D.Kinematic;
            pickRigidbody.linearVelocity = Vector2.zero;
            pickRigidbody.angularVelocity = 0f;
            pickRigidbody.WakeUp();
        }

        private void UpdatePickReturn()
        {
            float deltaTime = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;
            pickReturnElapsed += deltaTime;

            Vector2 restPosition = pickRestPosition;
            Vector2 nextPosition = Vector2.MoveTowards(
                pickRigidbody.position,
                restPosition,
                PickReturnSpeed * deltaTime);
            float nextRotation = Mathf.MoveTowardsAngle(
                pickRigidbody.rotation,
                pickRestRotation,
                PickReturnRotationSpeed * deltaTime);

            pickRigidbody.position = nextPosition;
            pickRigidbody.rotation = nextRotation;
            pickRigidbody.linearVelocity = Vector2.zero;
            pickRigidbody.angularVelocity = 0f;

            bool positionReached = (nextPosition - restPosition).sqrMagnitude <=
                PickReturnSnapDistance * PickReturnSnapDistance;
            bool rotationReached = Mathf.Abs(Mathf.DeltaAngle(nextRotation, pickRestRotation)) <= 0.1f;
            if ((positionReached && rotationReached) || pickReturnElapsed >= PickReturnMaxSeconds)
            {
                ReturnPickToRest();
            }
        }

        private void UpdatePointerInput()
        {
            if (!entrySplashComplete || solved || exitingToRoom)
            {
                return;
            }

            PointerSnapshot pointer = ReadPointer();
            if (pointer.Released)
            {
                if (draggingPick)
                {
                    ReleasePick();
                }

                return;
            }

            if (!pointer.HasPointer)
            {
                if (draggingPick)
                {
                    ReleasePick();
                }

                return;
            }

            if (pointer.Pressed)
            {
                HandlePointerPressed(pointer.Position);
            }

            if (draggingPick && !pointer.Held)
            {
                ReleasePick();
                return;
            }

            if (draggingPick)
            {
                DragPick(pointer.Position);
            }

        }

        private void ReleasePick()
        {
            draggingPick = false;
            activeContactPin = -1;
            TouchSfxRouter.OverrideCurrentTouch(TouchSfxPreset.Silent);
            SoundPlayer.PlayLockPickPickDownSfx();
            ReturnPickToRest();
        }

        // 미니게임 전용 시작 스플래시가 끝나기 전에는 해정 조작을 받지 않는다.
        private async UniTaskVoid PlayEntryStartFlow(CancellationToken ct)
        {
            try
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(gameObject.scene);
                await SceneTransitionFadeUI.WaitForSpiralIdleAsync(ct);
                await StartSplashUI.PlayOnActiveCanvasAsync(LocalizationService.Text(EntryStartTid), ct);
                await TutorialPanelUI.ShowOnceAsync(TutorialPanelUI.TutorialId.LockPick, ct);
                ct.ThrowIfCancellationRequested();
                entrySplashComplete = true;
                SetGameplayInteractable(true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }

        private void SetGameplayInteractable(bool interactable)
        {
            if (retryButton != null)
            {
                retryButton.interactable = interactable;
            }
        }

        // 일반 락픽은 중도 이탈을 허용하고 수갑 튜토리얼에서만 Exit 버튼을 숨긴다.
        private void ConfigureExitButtonAvailability()
        {
            if (exitButton == null)
            {
                return;
            }

            exitButton.gameObject.SetActive(!isHandcuffsTutorial);
            exitButton.interactable = !isHandcuffsTutorial;
        }

        private void HandlePointerPressed(Vector2 screenPosition)
        {
            if (TryGetPointerWorld(screenPosition, out Vector3 worldPosition))
            {
                SoundPlayer.PlayLockPickPickUpSfx();
                draggingPick = true;
                returningPick = false;
                SetPickCollisionEnabled(true);
                pickRigidbody.bodyType = RigidbodyType2D.Dynamic;
                pickDragOffset = worldPosition - pickRoot.position;
                pickHandleJoint.enabled = true;
                pickRigidbody.WakeUp();
                DragPickAtWorld(worldPosition);
            }
        }

        private void DragPick(Vector2 screenPosition)
        {
            if (!TryGetPointerWorld(screenPosition, out Vector3 worldPosition))
            {
                return;
            }

            DragPickAtWorld(worldPosition);
        }

        private void DragPickAtWorld(Vector3 worldPosition)
        {
            Vector3 tipOffset = pickTip.position - pickRoot.position;
            float x = worldPosition.x - pickDragOffset.x;
            float y = worldPosition.y - pickDragOffset.y;
            float tipX = x + tipOffset.x;
            SetPickJointTarget(new Vector2(x, y), pickGripMaxForce);

            int nextContactPin = FindContactPin(tipX);
            if (nextContactPin != activeContactPin)
            {
                activeContactPin = nextContactPin;
            }
        }

        private void SetPickJointTarget(Vector2 target, float maxForce)
        {
            pickHandleJoint.target = target;
            pickHandleJoint.maxForce = maxForce;
        }

        private void SetPickCollisionEnabled(bool enabled)
        {
            if (pickColliders == null)
            {
                return;
            }

            for (int i = 0; i < pickColliders.Length; i++)
            {
                Collider2D pickCollider = pickColliders[i];
                if (pickCollider != null)
                {
                    pickCollider.enabled = enabled;
                }
            }
        }

        private bool TryGetPointerWorld(Vector2 screenPosition, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (inputCamera == null)
            {
                return false;
            }

            Ray ray = inputCamera.ScreenPointToRay(screenPosition);
            if (!dragPlane.Raycast(ray, out float enter))
            {
                return false;
            }

            worldPosition = ray.GetPoint(enter);
            return true;
        }

        private int FindContactPin(float tipX)
        {
            int closestIndex = -1;
            float closestDistance = contactRadius;
            for (int i = 0; i < pins.Length; i++)
            {
                float distance = Mathf.Abs(tipX - pins[i].Root.position.x);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        private void UpdatePinState()
        {
            for (int i = 0; i < pins.Length; i++)
            {
                ClampPinToLowerLimit(i);
                ClampPinToUpperLimit(i);
                LockSettledSetPin(i);
                currentLifts[i] = CalculatePinLift(i);
            }

            if (draggingPick)
            {
                int primaryLiftPin = FindPrimaryLiftPin();
                for (int i = 0; i < pins.Length; i++)
                {
                    if (ShouldLimitInactivePin(i, primaryLiftPin))
                    {
                        LimitInactivePinLift(i);
                    }

                    ClampPinToLowerLimit(i);
                    ClampPinToUpperLimit(i);
                    currentLifts[i] = CalculatePinLift(i);
                }

                EvaluateLiftedPins();
            }
        }

        // 스냅 반동이 끝난 고정 핀은 물리 영향을 받지 않도록 정확한 높이에 잠근다.
        private void LockSettledSetPin(int pinIndex)
        {
            if (!pinSet[pinIndex])
            {
                return;
            }

            Rigidbody2D body = pins[pinIndex].Body;
            if (body.bodyType == RigidbodyType2D.Kinematic)
            {
                return;
            }

            float setY = GetPinSetY(pinIndex);
            if (body.position.y > setY + pinSetLockThreshold)
            {
                return;
            }

            Vector2 position = body.position;
            position.y = setY;
            body.position = position;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
        }

        private void ClampPinToLowerLimit(int pinIndex)
        {
            Rigidbody2D body = pins[pinIndex].Body;
            float minY = GetPinMinimumY(pinIndex);
            if (body.position.y >= minY)
            {
                return;
            }

            Vector2 position = body.position;
            position.y = minY;
            body.position = position;

            Vector2 velocity = body.linearVelocity;
            if (velocity.y < 0f)
            {
                velocity.y = 0f;
                body.linearVelocity = velocity;
            }
        }

        private void ClampPinToUpperLimit(int pinIndex)
        {
            if (pinMaximumPositions.Length <= pinIndex)
            {
                return;
            }

            Rigidbody2D body = pins[pinIndex].Body;
            float maxY = Mathf.Max(GetPinMinimumY(pinIndex), pinMaximumPositions[pinIndex]);
            if (body.position.y <= maxY)
            {
                return;
            }

            Vector2 position = body.position;
            position.y = maxY;
            body.position = position;

            Vector2 velocity = body.linearVelocity;
            if (velocity.y > 0f)
            {
                velocity.y = 0f;
                body.linearVelocity = velocity;
            }
        }

        private float GetPinMinimumY(int pinIndex) =>
            pinSet[pinIndex] ? GetPinSetY(pinIndex) : pinBasePositions[pinIndex].y;

        private float GetPinSetY(int pinIndex) =>
            pinBasePositions[pinIndex].y + requiredLifts[pinIndex] * pinTravel;

        private float CalculatePinLift(int pinIndex) =>
            Mathf.Clamp01((pins[pinIndex].Root.position.y - pinBasePositions[pinIndex].y) / pinTravel);

        private int FindPrimaryLiftPin()
        {
            if (activeContactPin >= 0 && activeContactPin < pins.Length)
            {
                return activeContactPin;
            }

            int primaryPin = -1;
            float bestLift = 0f;
            for (int i = 0; i < currentLifts.Length; i++)
            {
                if (pinSet[i] || currentLifts[i] <= bestLift)
                {
                    continue;
                }

                primaryPin = i;
                bestLift = currentLifts[i];
            }

            return primaryPin;
        }

        private bool ShouldLimitInactivePin(int pinIndex, int primaryLiftPin) =>
            draggingPick &&
            primaryLiftPin >= 0 &&
            !pinSet[pinIndex] &&
            pinIndex != primaryLiftPin;

        private void LimitInactivePinLift(int pinIndex)
        {
            Rigidbody2D body = pins[pinIndex].Body;
            float maxY = pinBasePositions[pinIndex].y + inactivePinLiftLimit;
            if (body.position.y <= maxY)
            {
                return;
            }

            Vector2 position = body.position;
            position.y = maxY;
            body.position = position;

            Vector2 velocity = body.linearVelocity;
            if (velocity.y > 0f)
            {
                velocity.y = 0f;
                body.linearVelocity = velocity;
            }
        }

        private void EvaluateLiftedPins()
        {
            for (int i = 0; i < pins.Length; i++)
            {
                if (EvaluateLiftedPin(i))
                {
                    return;
                }
            }
        }

        private bool EvaluateLiftedPin(int pinIndex)
        {
            if (pinSet[pinIndex] || solved)
            {
                return false;
            }

            float clickDistance = Mathf.Min(setTolerance * pinTravel, PinSetClickDistance);
            bool liftedToClickHeight = pins[pinIndex].Body.position.y >= GetPinSetY(pinIndex) - clickDistance;
            if (releaseBlockedPins.Length > pinIndex && releaseBlockedPins[pinIndex])
            {
                if (!liftedToClickHeight)
                {
                    releaseBlockedPins[pinIndex] = false;
                }

                return false;
            }

            if (!liftedToClickHeight)
            {
                return false;
            }

            int releaseStartIndex = FindReleaseTailStartIndex(pinIndex);
            if (releaseStartIndex < sequenceIndex)
            {
                ReleaseSetPinsFromSequenceIndex(releaseStartIndex);
                SetPin(pinIndex);
                return true;
            }

            SetPin(pinIndex);
            return true;
        }

        private void SetPin(int pinIndex)
        {
            pinSet[pinIndex] = true;
            if (releaseBlockedPins.Length > pinIndex)
            {
                releaseBlockedPins[pinIndex] = false;
            }

            Rigidbody2D body = pins[pinIndex].Body;
            Vector3 setPosition = pinBasePositions[pinIndex] + Vector3.up * (requiredLifts[pinIndex] * pinTravel);
            body.bodyType = RigidbodyType2D.Dynamic;
            SnapSetPinIntoLock(pinIndex, body, setPosition.y);
            body.WakeUp();
            currentLifts[pinIndex] = requiredLifts[pinIndex];
            if (sequenceIndex >= 0 && sequenceIndex < setSequence.Length)
            {
                setSequence[sequenceIndex] = pinIndex;
            }

            sequenceIndex++;
            SoundPlayer.PlayLockPickPinSetSfx();

            if (AllPinsSet())
            {
                FinishSuccess();
                return;
            }

        }

        // 고정 순간 핀을 안쪽으로 한 번 더 밀어 넣고 곧바로 고정 높이에 내려앉게 한다.
        private void SnapSetPinIntoLock(int pinIndex, Rigidbody2D body, float setY)
        {
            float maximumY = pinMaximumPositions.Length > pinIndex
                ? pinMaximumPositions[pinIndex]
                : setY + pinSetSnapDistance;
            float snapY = Mathf.Clamp(setY + pinSetSnapDistance, setY, maximumY);

            Vector2 position = body.position;
            position.y = snapY;
            body.position = position;

            Vector2 velocity = body.linearVelocity;
            velocity.y = -pinSetSettleSpeed;
            body.linearVelocity = velocity;
            body.angularVelocity = 0f;
        }

        // 정답 순서가 뒤로 꺾이면 맞게 이어진 앞부분은 남기고 꼬인 꼬리만 푼다.
        private int FindReleaseTailStartIndex(int pinIndex)
        {
            int pinOrderIndex = GetPinOrderIndex(pinIndex);
            for (int i = 0; i < sequenceIndex; i++)
            {
                int setPinIndex = setSequence[i];
                if (GetPinOrderIndex(setPinIndex) > pinOrderIndex)
                {
                    return i;
                }
            }

            return sequenceIndex;
        }

        private int GetPinOrderIndex(int pinIndex)
        {
            if (pinIndex < 0 || pinIndex >= pinOrderIndices.Length)
            {
                return int.MaxValue;
            }

            return pinOrderIndices[pinIndex];
        }

        private void ClearSetSequence()
        {
            for (int i = 0; i < setSequence.Length; i++)
            {
                setSequence[i] = -1;
            }
        }

        private void ReleaseSetPinsFromSequenceIndex(int startIndex)
        {
            if (startIndex < 0 || startIndex >= sequenceIndex)
            {
                return;
            }

            for (int i = sequenceIndex - 1; i >= startIndex; i--)
            {
                ReleaseSetPin(setSequence[i]);
                setSequence[i] = -1;
            }

            sequenceIndex = startIndex;
        }

        private void ReleaseSetPin(int pinIndex)
        {
            if (pinIndex < 0 || pinIndex >= pinSet.Length || !pinSet[pinIndex])
            {
                return;
            }

            pinSet[pinIndex] = false;
            if (releaseBlockedPins.Length > pinIndex)
            {
                releaseBlockedPins[pinIndex] = true;
            }

            Rigidbody2D body = pins[pinIndex].Body;
            body.bodyType = RigidbodyType2D.Dynamic;
            Vector2 velocity = body.linearVelocity;
            if (velocity.y > 0f)
            {
                velocity.y = 0f;
            }

            body.linearVelocity = velocity;
            body.angularVelocity = 0f;
            body.WakeUp();
            currentLifts[pinIndex] = CalculatePinLift(pinIndex);
        }

        private void RecalculatePinMaximumPositions()
        {
            int count = Mathf.Min(pins.Length, pinBasePositions.Length, requiredLifts.Length);
            if (pinMaximumPositions.Length != pins.Length)
            {
                pinMaximumPositions = new float[pins.Length];
            }

            for (int i = 0; i < count; i++)
            {
                pinMaximumPositions[i] = CalculatePinMaximumPosition(i);
            }
        }

        private float CalculatePinMaximumPosition(int pinIndex)
        {
            float fallbackMaxY = pinBasePositions[pinIndex].y + pinTravel;
            float setY = GetPinSetY(pinIndex);
            if (pinStopColliders == null || pinStopColliders.Length == 0)
            {
                return Mathf.Max(setY, fallbackMaxY);
            }

            Bounds pinBounds = GetPinBounds(pinIndex);
            if (pinBounds.size == Vector3.zero)
            {
                return Mathf.Max(setY, fallbackMaxY);
            }

            float topOffset = pinBounds.max.y - pins[pinIndex].Root.position.y;
            float stopBottomY = float.PositiveInfinity;
            for (int i = 0; i < pinStopColliders.Length; i++)
            {
                Collider2D stopCollider = pinStopColliders[i];
                if (stopCollider == null || stopCollider.isTrigger)
                {
                    continue;
                }

                Bounds stopBounds = stopCollider.bounds;
                if (stopBounds.max.x <= pinBounds.min.x || stopBounds.min.x >= pinBounds.max.x)
                {
                    continue;
                }

                if (stopBounds.min.y <= pinBasePositions[pinIndex].y)
                {
                    continue;
                }

                stopBottomY = Mathf.Min(stopBottomY, stopBounds.min.y);
            }

            if (float.IsPositiveInfinity(stopBottomY))
            {
                return Mathf.Max(setY, fallbackMaxY);
            }

            float blockLimitedY = stopBottomY - topOffset - pinStopSkin;
            return Mathf.Max(setY, Mathf.Min(fallbackMaxY, blockLimitedY));
        }

        private Bounds GetPinBounds(int pinIndex)
        {
            Collider2D[] colliders = pins[pinIndex].Root.GetComponentsInChildren<Collider2D>();
            Bounds bounds = default;
            bool hasBounds = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(collider.bounds);
            }

            return hasBounds ? bounds : default;
        }

        private bool AllPinsSet()
        {
            for (int i = 0; i < pinSet.Length; i++)
            {
                if (!pinSet[i])
                {
                    return false;
                }
            }

            return true;
        }
        private void FinishSuccess()
        {
            solved = true;
            draggingPick = false;
            activeContactPin = -1;
            SetPickCollisionEnabled(false);
            SoundPlayer.PlayLockPickSuccessSfx();
            PlaySuccessSequence().Forget();
        }

        private void UpdatePinVisuals()
        {
            for (int i = 0; i < pins.Length; i++)
            {
                float lift = currentLifts.Length > i ? currentLifts[i] : 0f;
                Vector3 springScale = springBaseScales[i];
                springScale.y = Mathf.Max(0.12f, springBaseScales[i].y - lift * pinTravel * 0.58f);
                pins[i].SpringVisual.localScale = springScale;
                pins[i].SpringVisual.position = springBasePositions[i];

                Color lowerColor = pinSet[i]
                    ? setLowerPinColor
                    : lowerPinDefaultColors[i];
                Color upperColor = pinSet[i]
                    ? setUpperPinColor
                    : upperPinDefaultColors[i];
                if (successPinSweepActive && i == successPinSweepIndex)
                {
                    lowerColor = successPinSweepLowerColor;
                    upperColor = successPinSweepUpperColor;
                }

                SetRendererColor(pins[i].LowerPinRenderer, lowerColor);
                SetRendererColor(pins[i].UpperPinRenderer, upperColor);
                UpdateLowerPinBottomShapeVisual(i, lowerColor);

            }

        }

        private void ApplyLowerPinBottomShapes()
        {
            EnsureLowerPinBottomShapeSprites();
            for (int i = 0; i < pins.Length; i++)
            {
                ApplyLowerPinBottomShapeGeometry(i);
                UpdateLowerPinBottomShapeVisual(i, GetRendererColor(pins[i].LowerPinRenderer));
            }
        }

        private void ApplyLowerPinBottomShapeGeometry(int pinIndex)
        {
            Transform lowerPin = pins[pinIndex].LowerPin;
            if (lowerPin == null)
            {
                return;
            }

            if (pins[pinIndex].LowerPinRenderer is SpriteRenderer lowerRenderer &&
                lowerPinDefaultSprites.Length > pinIndex &&
                lowerPinDefaultSprites[pinIndex] != null)
            {
                lowerRenderer.sprite = lowerPinDefaultSprites[pinIndex];
            }

            Vector3 scale = lowerPin.localScale;
            Vector3 position = lowerPin.localPosition;
            float height = Mathf.Abs(scale.y);
            float capHeight = Mathf.Min(
                CalculateLowerPinBottomShapeHeight(scale),
                Mathf.Max(0f, height - 0.05f));
            if (capHeight <= 0f)
            {
                return;
            }

            float topY = position.y + height * 0.5f;
            float bodyHeight = height - capHeight;
            scale.y = Mathf.Sign(scale.y == 0f ? 1f : scale.y) * bodyHeight;
            position.y = topY - bodyHeight * 0.5f;

            lowerPin.localScale = scale;
            lowerPin.localPosition = position;
            lowerPinBottomShapeHeights[pinIndex] = capHeight;
        }

        private void UpdateLowerPinBottomShapeVisual(int pinIndex, Color color)
        {
            SpriteRenderer rootRenderer = pins[pinIndex].LowerPinBottomRenderer;
            if (rootRenderer == null)
            {
                return;
            }

            EnsureLowerPinBottomShapeSprites();
            SpriteRenderer[] presetRenderers = EnsureLowerPinBottomPresetRenderers(pinIndex);
            PinBottomShape shape = lowerPinBottomShapes.Length > pinIndex
                ? lowerPinBottomShapes[pinIndex]
                : PinBottomShape.ConvexTriangle;
            int shapeIndex = Mathf.Clamp((int)shape, 0, lowerPinBottomShapeSprites.Length - 1);
            bool hasPresetChildren = false;

            if (pins[pinIndex].LowerPinRenderer is SpriteRenderer lowerRenderer)
            {
                rootRenderer.sharedMaterial = lowerRenderer.sharedMaterial;
                rootRenderer.sortingLayerID = lowerRenderer.sortingLayerID;
                rootRenderer.sortingOrder = lowerRenderer.sortingOrder + 1;
            }

            Transform lowerPin = pins[pinIndex].LowerPin;
            Vector3 lowerScale = lowerPin.localScale;
            float bodyHeight = Mathf.Abs(lowerScale.y);
            float capHeight = lowerPinBottomShapeHeights.Length > pinIndex &&
                lowerPinBottomShapeHeights[pinIndex] > 0f
                    ? lowerPinBottomShapeHeights[pinIndex]
                    : CalculateLowerPinBottomShapeHeight(lowerScale);
            float capWidth = CalculateLowerPinBottomShapeWidth(lowerScale);
            float bodyBottomY = lowerPin.localPosition.y - bodyHeight * 0.5f;

            Transform visual = rootRenderer.transform;
            visual.localRotation = Quaternion.identity;
            visual.localScale = new Vector3(capWidth, capHeight, 1f);
            visual.localPosition = new Vector3(
                lowerPin.localPosition.x,
                bodyBottomY - capHeight * 0.5f + PinBottomShapeOverlap,
                lowerPin.localPosition.z);

            for (int i = 0; i < presetRenderers.Length; i++)
            {
                SpriteRenderer presetRenderer = presetRenderers[i];
                if (presetRenderer == null)
                {
                    continue;
                }

                hasPresetChildren = true;
                if (presetRenderer.sprite == null)
                {
                    presetRenderer.sprite = lowerPinBottomShapeSprites[i];
                }

                presetRenderer.color = color;
                presetRenderer.sharedMaterial = rootRenderer.sharedMaterial;
                presetRenderer.sortingLayerID = rootRenderer.sortingLayerID;
                presetRenderer.sortingOrder = rootRenderer.sortingOrder;
                presetRenderer.transform.localPosition = Vector3.zero;
                presetRenderer.transform.localRotation = Quaternion.identity;
                presetRenderer.transform.localScale = Vector3.one;
                SetVisible(presetRenderer, i == shapeIndex);
            }

            rootRenderer.gameObject.SetActive(true);
            rootRenderer.enabled = !hasPresetChildren;
            if (!hasPresetChildren)
            {
                SetColliderEnabled(rootRenderer, true);
                rootRenderer.sprite = lowerPinBottomShapeSprites[shapeIndex];
                rootRenderer.color = color;
            }
        }

        private SpriteRenderer[] EnsureLowerPinBottomPresetRenderers(int pinIndex)
        {
            if (lowerPinBottomPresetRenderers.Length <= pinIndex)
            {
                return Array.Empty<SpriteRenderer>();
            }

            SpriteRenderer[] cachedRenderers = lowerPinBottomPresetRenderers[pinIndex];
            if (cachedRenderers != null && cachedRenderers.Length == PinBottomShapeCount)
            {
                return cachedRenderers;
            }

            SpriteRenderer rootRenderer = pins[pinIndex].LowerPinBottomRenderer;
            if (rootRenderer == null)
            {
                return Array.Empty<SpriteRenderer>();
            }

            var renderers = new SpriteRenderer[PinBottomShapeCount];
            SpriteRenderer[] childRenderers = rootRenderer.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < childRenderers.Length; i++)
            {
                SpriteRenderer childRenderer = childRenderers[i];
                if (childRenderer == null || childRenderer == rootRenderer)
                {
                    continue;
                }

                if (TryGetLowerPinBottomShapeIndex(childRenderer.gameObject.name, out int shapeIndex))
                {
                    renderers[shapeIndex] = childRenderer;
                    continue;
                }

                SetVisible(childRenderer, false);
            }

            lowerPinBottomPresetRenderers[pinIndex] = renderers;
            return renderers;
        }

        private static bool TryGetLowerPinBottomShapeIndex(string name, out int shapeIndex)
        {
            if (name == nameof(PinBottomShape.ConvexTriangle))
            {
                shapeIndex = (int)PinBottomShape.ConvexTriangle;
                return true;
            }

            shapeIndex = -1;
            return false;
        }

        private static void SetVisible(SpriteRenderer renderer, bool visible)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.enabled = visible;
            SetColliderEnabled(renderer, visible);
            renderer.gameObject.SetActive(visible);
        }

        private static void SetColliderEnabled(SpriteRenderer renderer, bool enabled)
        {
            Collider2D[] colliders = renderer.GetComponents<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = enabled;
            }
        }

        private static float CalculateLowerPinBottomShapeWidth(Vector3 lowerScale) =>
            Mathf.Max(0.03f, Mathf.Abs(lowerScale.x));

        private static float CalculateLowerPinBottomShapeHeight(Vector3 lowerScale)
        {
            float height = Mathf.Abs(lowerScale.y);
            return Mathf.Max(0.024f, height * 0.28f);
        }

        private void EnsureLowerPinBottomShapeSprites()
        {
            if (lowerPinBottomShapeSprites.Length == PinBottomShapeCount)
            {
                return;
            }

            lowerPinBottomShapeSprites = new Sprite[PinBottomShapeCount];
            for (int i = 0; i < lowerPinBottomShapeSprites.Length; i++)
            {
                lowerPinBottomShapeSprites[i] = CreateLowerPinBottomShapeSprite();
            }
        }

        private static Sprite CreateLowerPinBottomShapeSprite()
        {
            var texture = new Texture2D(
                PinBottomShapeTextureSize,
                PinBottomShapeTextureSize,
                TextureFormat.RGBA32,
                false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color clear = Color.clear;
            Color fill = Color.white;
            for (int y = 0; y < PinBottomShapeTextureSize; y++)
            {
                for (int x = 0; x < PinBottomShapeTextureSize; x++)
                {
                    texture.SetPixel(x, y, IsLowerPinBottomShapePixelFilled(x, y) ? fill : clear);
                }
            }

            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, PinBottomShapeTextureSize, PinBottomShapeTextureSize),
                new Vector2(0.5f, 0.5f),
                PinBottomShapeTextureSize);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static bool IsLowerPinBottomShapePixelFilled(int x, int y)
        {
            float normalizedX = ((x + 0.5f) / PinBottomShapeTextureSize) * 2f - 1f;
            float normalizedY = (y + 0.5f) / PinBottomShapeTextureSize;
            return normalizedY >= GetLowerPinBottomEdge(normalizedX);
        }

        private static float GetLowerPinBottomEdge(float normalizedX)
        {
            return PinBottomShapeHeightRatio * Mathf.Abs(normalizedX);
        }

        private void ExitToRoom()
        {
            if (exitingToRoom)
            {
                return;
            }

            exitingToRoom = true;
            solved = true;
            draggingPick = false;
            showingSuccess = false;
            SoundPlayer.StopBgm();
            if (exitButton != null)
            {
                exitButton.interactable = false;
            }

            if (retryButton != null)
            {
                retryButton.interactable = false;
            }

            ExitToRoomAfterTransition().Forget();
        }

        private void BindButtons()
        {
            if (retryButton != null)
            {
                retryButton.onClick.RemoveListener(OnRetryButtonClicked);
                retryButton.onClick.AddListener(OnRetryButtonClicked);
            }

            if (exitButton != null)
            {
                exitButton.onClick.RemoveListener(OnExitButtonClicked);
                exitButton.onClick.AddListener(OnExitButtonClicked);
            }
        }

        private void OnRetryButtonClicked()
        {
            if (exitingToRoom)
            {
                return;
            }

            RestartGame();
        }

        private void OnExitButtonClicked()
        {
            ExitToRoom();
        }

        // 성공 순간을 바로 전환하지 않고 짧은 피드백으로 보여준 뒤 방으로 돌아간다.
        private async UniTaskVoid PlaySuccessSequence()
        {
            if (showingSuccess || exitingToRoom)
            {
                return;
            }

            showingSuccess = true;
            if (exitButton != null)
            {
                exitButton.interactable = false;
            }

            if (retryButton != null)
            {
                retryButton.interactable = false;
            }

            try
            {
                await PlaySuccessPinSweep(destroyCancellationToken);
                await ShowSuccessFeedback(destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            SceneLoadArgs.RequestLockPickUnlock();
            ExitToRoom();
        }

        private async UniTask PlaySuccessPinSweep(CancellationToken ct)
        {
            if (pins.Length == 0 || successPinSweepStepSeconds <= 0f)
            {
                return;
            }

            successPinSweepActive = true;
            try
            {
                for (int i = 0; i < pins.Length; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    successPinSweepIndex = i;
                    UpdatePinVisuals();
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(successPinSweepStepSeconds),
                        DelayType.UnscaledDeltaTime,
                        PlayerLoopTiming.Update,
                        ct);
                }
            }
            finally
            {
                successPinSweepActive = false;
                successPinSweepIndex = -1;
                UpdatePinVisuals();
            }
        }

        private async UniTask ShowSuccessFeedback(CancellationToken ct)
        {
            if (successText == null && successCanvasGroup == null)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(successHoldSeconds),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    ct);
                return;
            }

            if (successText != null)
            {
                successText.text = string.Empty;
                successText.gameObject.SetActive(true);
            }

            if (successCanvasGroup != null)
            {
                successCanvasGroup.gameObject.SetActive(true);
                successCanvasGroup.alpha = 1f;
                successCanvasGroup.interactable = false;
                successCanvasGroup.blocksRaycasts = false;
            }

            RectTransform textRect = successText != null ? successText.rectTransform : null;
            Vector3 baseScale = textRect != null ? textRect.localScale : Vector3.one;
            if (textRect != null)
            {
                textRect.localScale = baseScale;
            }

            await RevealSuccessTextSteps(textRect, baseScale, ct);

            if (successHoldSeconds > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(successHoldSeconds),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    ct);
            }
        }

        private async UniTask RevealSuccessTextSteps(RectTransform textRect, Vector3 baseScale, CancellationToken ct)
        {
            if (successText == null)
            {
                return;
            }

            string message = GetLocalizedSuccessMessage();
            float stepSeconds = Mathf.Max(0.01f, successRevealSeconds / Mathf.Max(1, message.Length));
            for (int i = 1; i <= message.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                successText.text = message.Substring(0, i);

                if (textRect != null)
                {
                    textRect.localScale = baseScale * 1.06f;
                }

                await SettleSuccessTextStep(textRect, baseScale, stepSeconds, ct);
            }

            successText.text = message;
            if (textRect != null)
            {
                textRect.localScale = baseScale;
            }
        }

        private string GetLocalizedSuccessMessage() =>
            LocalizationService.Text(successTid, successMessage);

        private static async UniTask SettleSuccessTextStep(
            RectTransform textRect,
            Vector3 baseScale,
            float stepSeconds,
            CancellationToken ct)
        {
            if (textRect == null)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(stepSeconds),
                    DelayType.UnscaledDeltaTime,
                    PlayerLoopTiming.Update,
                    ct);
                return;
            }

            float elapsed = 0f;
            while (elapsed < stepSeconds)
            {
                ct.ThrowIfCancellationRequested();
                float t = Mathf.Clamp01(elapsed / stepSeconds);
                textRect.localScale = baseScale * Mathf.Lerp(1.06f, 1f, t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                elapsed += Time.unscaledDeltaTime;
            }

            textRect.localScale = baseScale;
        }

        private void HideSuccessFeedback()
        {
            showingSuccess = false;
            if (successCanvasGroup != null)
            {
                successCanvasGroup.alpha = 0f;
                successCanvasGroup.interactable = false;
                successCanvasGroup.blocksRaycasts = false;
                successCanvasGroup.gameObject.SetActive(false);
            }

            if (successText != null)
            {
                successText.gameObject.SetActive(false);
                successText.rectTransform.localScale = Vector3.one;
            }
        }

        private async UniTaskVoid ExitToRoomAfterTransition()
        {
            GameSession.Instance?.SetInputLocked(true, SceneLoadArgs.MiniGameInputLockReason);
            try
            {
                await SceneTransitionFadeUI.PlaySpiralTransitionAsync(
                    ExitMiniGameToRoom,
                    exitTransitionSeconds,
                    CancellationToken.None);
            }
            finally
            {
                EscapeSceneLoader.ReleaseMiniGameInputLock();
            }
        }

        private static void ExitMiniGameToRoom()
        {
            EscapeSceneLoader.ReturnRoomFromMiniGame();
        }

        private static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer != null)
            {
                if (renderer is SpriteRenderer spriteRenderer)
                {
                    spriteRenderer.color = color;
                    return;
                }

                if (renderer is LineRenderer lineRenderer)
                {
                    lineRenderer.startColor = color;
                    lineRenderer.endColor = color;
                    return;
                }

                renderer.sharedMaterial.color = color;
            }
        }

        private static Color GetRendererColor(Renderer renderer)
        {
            if (renderer is SpriteRenderer spriteRenderer)
            {
                return spriteRenderer.color;
            }

            if (renderer is LineRenderer lineRenderer)
            {
                return lineRenderer.startColor;
            }

            return renderer != null && renderer.sharedMaterial != null
                ? renderer.sharedMaterial.color
                : Color.white;
        }

        private PointerSnapshot ReadPointer()
        {
#if ENABLE_INPUT_SYSTEM
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                TouchControl touch = touchscreen.primaryTouch;
                if (touch.press.isPressed ||
                    touch.press.wasPressedThisFrame ||
                    touch.press.wasReleasedThisFrame)
                {
                    return new PointerSnapshot(
                        true,
                        touch.position.ReadValue(),
                        touch.press.wasPressedThisFrame,
                        touch.press.isPressed,
                        touch.press.wasReleasedThisFrame);
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                return new PointerSnapshot(
                    true,
                    mouse.position.ReadValue(),
                    mouse.leftButton.wasPressedThisFrame,
                    mouse.leftButton.isPressed,
                    mouse.leftButton.wasReleasedThisFrame);
            }
#else
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                return new PointerSnapshot(
                    true,
                    touch.position,
                    touch.phase == TouchPhase.Began,
                    touch.phase == TouchPhase.Moved ||
                        touch.phase == TouchPhase.Stationary ||
                        touch.phase == TouchPhase.Began,
                    touch.phase == TouchPhase.Ended ||
                        touch.phase == TouchPhase.Canceled);
            }

            return new PointerSnapshot(
                true,
                Input.mousePosition,
                Input.GetMouseButtonDown(0),
                Input.GetMouseButton(0),
                Input.GetMouseButtonUp(0));
#endif
            return default;
        }

        [Serializable]
        public sealed class PinBody
        {
            [SerializeField] private Transform root;
            [SerializeField] private Rigidbody2D body;
            [SerializeField] private SpringJoint2D springJoint;
            [SerializeField] private Transform springVisual;
            [SerializeField] private Transform upperPin;
            [SerializeField] private Transform lowerPin;
            [SerializeField] private Renderer upperPinRenderer;
            [SerializeField] private Renderer lowerPinRenderer;
            [SerializeField] private SpriteRenderer lowerPinBottomRenderer;

            public Transform Root => root;
            public Rigidbody2D Body => body;
            public SpringJoint2D SpringJoint => springJoint;
            public Transform SpringVisual => springVisual;
            public Transform UpperPin => upperPin;
            public Transform LowerPin => lowerPin;
            public Renderer UpperPinRenderer => upperPinRenderer;
            public Renderer LowerPinRenderer => lowerPinRenderer;
            public SpriteRenderer LowerPinBottomRenderer => lowerPinBottomRenderer;

            public bool HasRequiredReferences =>
                root != null &&
                body != null &&
                springJoint != null &&
                springVisual != null &&
                upperPin != null &&
                lowerPin != null &&
                upperPinRenderer != null &&
                lowerPinRenderer != null;

            public PinBody(
                Transform root,
                Rigidbody2D body,
                SpringJoint2D springJoint,
                Transform springVisual,
                Transform upperPin,
                Transform lowerPin,
                Renderer upperPinRenderer,
                Renderer lowerPinRenderer,
                SpriteRenderer lowerPinBottomRenderer = null)
            {
                this.root = root;
                this.body = body;
                this.springJoint = springJoint;
                this.springVisual = springVisual;
                this.upperPin = upperPin;
                this.lowerPin = lowerPin;
                this.upperPinRenderer = upperPinRenderer;
                this.lowerPinRenderer = lowerPinRenderer;
                this.lowerPinBottomRenderer = lowerPinBottomRenderer;
            }
        }

        private readonly struct PointerSnapshot
        {
            public readonly bool HasPointer;
            public readonly Vector2 Position;
            public readonly bool Pressed;
            public readonly bool Held;
            public readonly bool Released;

            public PointerSnapshot(bool hasPointer, Vector2 position, bool pressed, bool held, bool released)
            {
                HasPointer = hasPointer;
                Position = position;
                Pressed = pressed;
                Held = held;
                Released = released;
            }
        }
    }
}
