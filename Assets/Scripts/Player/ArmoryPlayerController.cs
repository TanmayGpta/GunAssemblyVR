using UnityEngine;

namespace VRArmory
{
    [RequireComponent(typeof(CharacterController))]
    public class ArmoryPlayerController : MonoBehaviour
    {
        [Header("Movement")]
        public float walkSpeed = 3.0f;
        public float sprintSpeed = 5.5f;
        public float gravity = 15.0f;

        [Header("Look Settings")]
        public float mouseSensitivity = 2.0f;
        public float minPitch = -75.0f;
        public float maxPitch = 75.0f;

        [Header("References")]
        public Camera playerCamera;
        // Set by interactors that borrow the mouse (e.g. rotating the off-hand part).
        [System.NonSerialized] public bool lookLocked;

        private CharacterController characterController;
        private float pitch = 0f;
        private float verticalVelocity = 0f;

        void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
            }
            if (playerCamera != null)
            {
                pitch = playerCamera.transform.localEulerAngles.x;
                if (pitch > 180f) pitch -= 360f;
            }
        }

        void Start()
        {
            LockCursor(true);
        }

        void Update()
        {
            HandleCursor();
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                if (!lookLocked) HandleLook();
                HandleMovement();
            }
        }

        private void HandleCursor()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                LockCursor(false);
            }
            else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                LockCursor(true);
            }
        }

        private void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void HandleLook()
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            // Horizontal yaw
            transform.Rotate(Vector3.up * mouseX);

            // Vertical pitch
            if (playerCamera != null)
            {
                pitch -= mouseY;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
                playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }
        }

        private void HandleMovement()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            bool isSprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;

            Vector3 inputVector = new Vector3(h, 0f, v);
            if (inputVector.sqrMagnitude > 1f)
            {
                inputVector.Normalize();
            }

            Vector3 moveDirection = transform.TransformDirection(inputVector);

            if (characterController.isGrounded)
            {
                verticalVelocity = -0.5f;
            }
            else
            {
                verticalVelocity -= gravity * Time.deltaTime;
            }

            Vector3 velocity = moveDirection * currentSpeed + Vector3.up * verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
        }
    }
}
