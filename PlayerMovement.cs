using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    // Player Input
    float x, y; // horizontal and vertical
    bool jumping, sprinting, crouching;

    [Header("Assignables")]
    [SerializeField] Transform orientation;
    [SerializeField] Transform lookCam;
    Rigidbody rb = null;

    [Header("Movement")]
    [SerializeField] float playerWeight;
    [SerializeField] float moveSpeed; // baseline movement speed
    float currentMaxSpeed; // stores current max speed
    [SerializeField] float maxWalkSpeed;
    [SerializeField] float maxSprintSpeed;
    [SerializeField] float maxAirSpeed;

    bool xTooFast = false; // x speeds too fast
    bool yTooFast = false; // y speeds too fast

    float speedMult;
    [SerializeField] float sprintMult;
    [SerializeField] float airMultiplier;

    [SerializeField] float accelAmt; // controls fastness of transition btwn speeds
    [SerializeField] float currentAccel; // value should stay between 1 & 2, will be used as a multiplier to transition between different speeds (public to monitor value in inspector)

    public bool grounded;
    public LayerMask groundLayer;
    [SerializeField] float groundDist;
    [SerializeField] Transform groundCheck;

    [SerializeField] float groundDrag; // to help control friction of player so it doesn't slide around
    [SerializeField] float airDrag;
    float currentDrag;

    float gravityMultiplier;
    [SerializeField] float standardGravityMult;

    // counter movement
    [SerializeField] float counterMovementForce = 600f;

    [Header("Crouch/Slide")]
    [SerializeField] float slideSpeed;
    [SerializeField] float crouchGravityMultiplier;
    private Vector3 crouchScale = new Vector3(1, 0.5f, 1);
    private Vector3 playerScale;

    [Header("Jump")]
    [SerializeField] float jumpForce;
    [SerializeField] float fwdMult; // how much forward momentum a jump grants
    [SerializeField] float upwardMult; // how much upward momentum a jump grants
    [SerializeField] float jumpCoolDown; // how long btwn jumps

    // Player states
    bool canJump = false;
    bool canSprint = false;
    bool alreadyJumped = false;

    [Header("Detection Whiskers")] // will send out whisker-like rays for collision detection to help with situations like slope, stairs, auto-vaulting, etc.
    [SerializeField] Transform topWhisker;
    [SerializeField] Transform bottomWhisker;
    [SerializeField] float whiskerRange; // how far the whiskers should extend 
    bool onSlope = false;
    [SerializeField] float slopeGravityMult;
    bool shouldAutoVault = false;

    [Header("Wall Running")]
    [SerializeField] LayerMask wallLayer;
    [SerializeField] float maxWallRunSpeed;
    [SerializeField] float wallRunForce;
    [SerializeField] float wallRunGravityMult;
    [SerializeField] float sidewaysJumpMultiplier;
    [SerializeField] float upwardJumpMultiplier;
    [SerializeField] float forwardJumpMultiplier;
    public bool wallRunning = false;
    public bool wallLeft = false;
    public bool wallRight = false;
    bool haveResetVelocity = false;
    bool runningUp = false;

    [Header("Climbing")]
    [SerializeField] float climbForce;
    [SerializeField] float climbDuration;
    [SerializeField] float climbCoolDown;
    bool canClimb = true;
    bool climbing = false;
    bool wallInFront = false;

    [Header("Audio")]
    [SerializeField] AudioSource stepSource;
    [SerializeField] AudioSource jumpSource;
    [SerializeField] AudioSource climbSource;

    private void Start()
    {
        rb = this.GetComponent<Rigidbody>();
        playerScale = transform.localScale;
        rb.mass = playerWeight;
    }

    private void Update()
    {
        PlayerInput();
        PlayerStates();
        GroundCheck();
        Whiskers();

        if (!grounded && !wallRunning)
            stepSource.Stop();
    }

    private void FixedUpdate()
    {
        Movement();
    }

    void PlayerInput()
    {
        x = Input.GetAxisRaw("Horizontal");
        y = Input.GetAxisRaw("Vertical");

        if (Mathf.Abs(x) > 0 || Mathf.Abs(y) > 0) // moving
        {
            if (grounded)
            {
                if (Input.GetKey(KeyCode.LeftShift) && canSprint) // sprinting
                {
                    sprinting = true;
                    currentMaxSpeed = maxSprintSpeed;
                    speedMult = sprintMult;
                    AccelerateWalkToSprint();
                    RunningAudio();
                }
                else // walking
                {
                    sprinting = false;
                    currentMaxSpeed = maxWalkSpeed;
                    speedMult = 1f;
                    DecelerateSprintToWalk();
                    stepSource.pitch = .5f;
                    WalkingAudio();
                }
            }
            else // not grounded
            {
                speedMult = airMultiplier;
                currentMaxSpeed = maxAirSpeed;
            }
        }
        else // specifically not moving
        {
            stepSource.Stop();
        }

        if (Input.GetButton("Jump")) // holding jump key
        {
            jumping = true;
        }
        else jumping = false;

        if (Input.GetKey(KeyCode.LeftControl)) // holding crouch key
        {
            crouching = true;
            transform.localScale = crouchScale;
        }
        else
        {
            crouching = false;
            transform.localScale = playerScale;
        }

        // Wall running
        if (wallLeft)
        {
            // only wall run while they hold the key
            if (x < 0)
            {
                StartWallRun();
                if (y > .01f)
                    runningUp = true;
                else runningUp = false;
            }
            else
                StopWallRun();
        }
        if (wallRight)
        {
            // only wall run while they hold the key
            if (x > 0)
            {
                StartWallRun();
                if (y > .01f)
                    runningUp = true;
                else runningUp = false;
            }
            else
                StopWallRun();
        }
        if ((!wallRight && !wallLeft) || (wallLeft && wallRight))
            StopWallRun();

        // climbing
        if (y > 0 && wallInFront) // moving forward
            climbing = true;
        else
            climbing = false;
    }

    void StartWallRun()
    {
        if (crouching)
        {
            StopWallRun();
        }

        ResetVelocity(false, true, false);

        rb.useGravity = false;
        wallRunning = true;

        canClimb = false; // fixes bug where player could climb and wall run up at same time, super fast upwards
        Invoke(nameof(ResetClimb), climbCoolDown);

        if (rb.velocity.magnitude < maxWallRunSpeed)
        {
            // standard force forward
            rb.AddForce(orientation.forward * wallRunForce * Time.deltaTime);

            if (runningUp)
                rb.AddForce(Vector3.up * wallRunForce / 1.5f * Time.deltaTime);

            // make player stick to wall
            if (wallLeft)
                rb.AddForce(-orientation.right * wallRunForce / 5 * Time.deltaTime);
            else
                rb.AddForce(orientation.right * wallRunForce / 5 * Time.deltaTime);
        }

        RunningAudio();
    }

    void StopWallRun()
    {
        rb.useGravity = true;
        wallRunning = false;
        haveResetVelocity = false;
    }

    void Movement()
    {
        // velocity relative to where player is looking
        Vector2 mag = FindVelRelativeToLook();
        float xMag = mag.x, yMag = mag.y;

        // max speed checks
        if ((x > 0 && (xMag < currentMaxSpeed)) || (x < 0 && (xMag > -currentMaxSpeed)))
            xTooFast = false;
        else
            xTooFast = true;

        if ((y > 0 && (yMag < currentMaxSpeed)) || (y < 0 && (yMag > -currentMaxSpeed)))
            yTooFast = false;
        else
            yTooFast = true;

        // standard movement
        if (!xTooFast)
            rb.AddForce(x * orientation.right * moveSpeed * speedMult * currentAccel * Time.deltaTime);
        if (!yTooFast)
            rb.AddForce(y * orientation.forward * moveSpeed * speedMult * currentAccel * Time.deltaTime);

        // counter movement
        CounterMovement();

        // Gravity handling
        if (crouching)
            gravityMultiplier = crouchGravityMultiplier;
        else if (onSlope)
            gravityMultiplier = slopeGravityMult;
        else if (wallRunning)
            gravityMultiplier = wallRunGravityMult;
        else
            gravityMultiplier = standardGravityMult;

        rb.AddForce(Vector3.down * gravityMultiplier * Time.deltaTime);

        // jumping
        if (jumping && canJump)
        {
            if (!alreadyJumped)
            {
                Jump();
                Invoke("ResetJump", jumpCoolDown);

                alreadyJumped = true;
            }
        }

        // auto-vault
        if (shouldAutoVault)
        {
            if (wallLeft)
                AutoVault(true);
            else
                AutoVault(false);
        }

        // climbing
        if (climbing && canClimb)
        {
            StartClimbing();
        }

        // rigidbody drag (friction basically)
        rb.drag = currentDrag;
    }

    void PlayerStates()
    {
        if (grounded)
        {
            canJump = true;

            if (!crouching) canSprint = true;

            currentDrag = groundDrag;
        }
        else if (wallRunning)
        {
            if (!alreadyJumped) canJump = true;

            currentDrag = airDrag;
        }
        else
        {
            canJump = false;
            canSprint = false;
            currentDrag = airDrag;
        }
    }

    void CounterMovement()
    {
        // limiting diagonal movement speed so it doesn't double up
        if ((y > 0 || y < 0) && (x < 0 || x > 0)) // if moving both directions (diagonally)
            rb.AddForce(counterMovementForce * -rb.velocity * Time.deltaTime); // add force in opposite direction of player
    }

    void AccelerateWalkToSprint()
    {
        if (currentAccel < 2f) currentAccel += Time.deltaTime * accelAmt;
        else currentAccel = 2f;
    }
    void DecelerateSprintToWalk()
    {
        if (currentAccel > 1f) currentAccel -= Time.deltaTime * accelAmt;
        else currentAccel = 1f;
    }

    void GroundCheck()
    {
        if (Physics.CheckSphere(groundCheck.position, groundDist, groundLayer) || Physics.CheckSphere(groundCheck.position, groundDist, wallLayer))
            grounded = true;
        else
            grounded = false;
    }

    void Jump()
    {
        // add jump forces
        if (!wallRunning)
        {
            rb.AddForce(jumpForce * Vector3.up * upwardMult, ForceMode.Impulse);
            rb.AddForce(jumpForce * orientation.forward * fwdMult, ForceMode.Impulse);

            jumpSource.Play();
        }
        else // wallrunning
        {
            rb.AddForce(jumpForce * Vector3.up * upwardJumpMultiplier, ForceMode.Impulse); // less upward force
            rb.AddForce(jumpForce * orientation.forward * forwardJumpMultiplier, ForceMode.Impulse); // more fwd force

            // sideways force - opposite to wall
            if (wallLeft)
                rb.AddForce(jumpForce * orientation.right * sidewaysJumpMultiplier, ForceMode.Impulse);
            else
                rb.AddForce(jumpForce * -orientation.right * sidewaysJumpMultiplier, ForceMode.Impulse);

            jumpSource.Play();
        }
    }

    void ResetJump()
    {
        alreadyJumped = false;
    }

    void StartClimbing()
    {
        rb.useGravity = false;
        ResetVelocity(true, true, true);

        // climbing forces
        rb.AddForce(climbForce * Vector3.up * Time.deltaTime);

        Invoke(nameof(StopClimbing), climbDuration);

        if (!wallInFront)
            StopClimbing();

        if (!climbSource.isPlaying)
            climbSource.Play();
    }

    void StopClimbing()
    {
        canClimb = false;
        rb.useGravity = true;
        haveResetVelocity = false;
        Invoke(nameof(ResetClimb), climbCoolDown);
        climbSource.Stop();
    }

    void ResetClimb()
    {
        canClimb = true;
    }

    void Whiskers() // whisker-like collision detection / Raycasts
    {
        RaycastHit hit;

        // slope detection
        if (Physics.Raycast(bottomWhisker.position, orientation.forward, out hit, whiskerRange, groundLayer)
            && !Physics.Raycast(topWhisker.position, orientation.forward, out hit, whiskerRange, groundLayer))
        {
            // player is trying to walk up a slope
            onSlope = true;
        }
        else onSlope = false;

        // wall detection
        wallRight = Physics.Raycast(orientation.position, orientation.right, out hit, whiskerRange, wallLayer);
        wallLeft = Physics.Raycast(orientation.position, -orientation.right, out hit, whiskerRange, wallLayer);

        // Auto-vault : player must be trying to move in the direction of the auto-vault for it to activate
        // auto-vault right
        if ((Physics.Raycast(bottomWhisker.position, orientation.right, out hit, whiskerRange, wallLayer) &&
            !Physics.Raycast(topWhisker.position, orientation.right, out hit, whiskerRange, wallLayer) && x > 0) ||
        // auto-vault left
            (Physics.Raycast(bottomWhisker.position, -orientation.right, out hit, whiskerRange, wallLayer) &&
            !Physics.Raycast(topWhisker.position, -orientation.right, out hit, whiskerRange, wallLayer) && x < 0) ||
        // auto-vault forward
            (Physics.Raycast(bottomWhisker.position, orientation.forward, out hit, whiskerRange, wallLayer) &&
            !Physics.Raycast(topWhisker.position, orientation.forward, out hit, whiskerRange, wallLayer)) && y > 0)
                shouldAutoVault = true;
        else
            shouldAutoVault = false;


        // climbing
        wallInFront = Physics.Raycast(orientation.position, orientation.forward, out hit, whiskerRange, wallLayer);
    }

    void AutoVault(bool isLeft)
    {
        if (isLeft)
            rb.AddForce(jumpForce / 8f * Vector3.up, ForceMode.Impulse);
        else 
            rb.AddForce(jumpForce / 8f * Vector3.up, ForceMode.Impulse);

        //Debug.Log("Trying to auto vault");
    }

    void ResetVelocity(bool x, bool y, bool z) // resets velocity on whichever axis 
    {
        if (!haveResetVelocity)
        {
            Vector3 velocity = rb.velocity;

            if (x) velocity.x = 0f;
            if (y) velocity.y = 0f;
            if (z) velocity.z = 0f;

            rb.velocity = velocity;
            haveResetVelocity = true;
        }
    }

    void WalkingAudio()
    {
        stepSource.pitch = .5f;
        if (!stepSource.isPlaying)
            stepSource.Play();
    }

    void RunningAudio()
    {
        stepSource.pitch = .75f;
        if (!stepSource.isPlaying) stepSource.Play();
    }

    // ** code obtained from Dani's FPS movement tutorial **
    // Find the velocity relative to where the player is looking
    // Useful for vectors calculations regarding movement and limiting movement
    public Vector2 FindVelRelativeToLook()
    {
        float lookAngle = orientation.eulerAngles.y;
        float moveAngle = Mathf.Atan2(rb.velocity.x, rb.velocity.z) * Mathf.Rad2Deg;

        float u = Mathf.DeltaAngle(lookAngle, moveAngle);
        float v = 90 - u;

        float magnitue = rb.velocity.magnitude;
        float yMag = magnitue * Mathf.Cos(u * Mathf.Deg2Rad);
        float xMag = magnitue * Mathf.Cos(v * Mathf.Deg2Rad);

        return new Vector2(xMag, yMag);
    }
}
