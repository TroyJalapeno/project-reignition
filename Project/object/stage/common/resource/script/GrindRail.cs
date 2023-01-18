using Godot;
using Godot.Collections;
using Project.Core;

namespace Project.Gameplay
{
	/// <summary>
	/// Object that controls how grinding works. Keep in mind grinding backwards isn't supported.
	/// </summary>
	[Tool]
	public partial class GrindRail : Area3D
	{
		#region Editor
		public override Array<Dictionary> _GetPropertyList()
		{
			Array<Dictionary> properties = new Array<Dictionary>();

			properties.Add(ExtensionMethods.CreateProperty("Rail Path", Variant.Type.NodePath, PropertyHint.NodePathValidTypes, "Path3D"));
			properties.Add(ExtensionMethods.CreateProperty("Invisible Rail Settings/Enabled", Variant.Type.Bool));

			if (isInvisibleRail)
			{
				properties.Add(ExtensionMethods.CreateProperty("Invisible Rail Settings/Length", Variant.Type.Int, PropertyHint.Range, "5,120"));

				properties.Add(ExtensionMethods.CreateProperty("Invisible Rail Settings/Rail Object", Variant.Type.NodePath, PropertyHint.NodePathValidTypes, "Node3D"));
				properties.Add(ExtensionMethods.CreateProperty("Invisible Rail Settings/Rail Material", Variant.Type.Object));
				properties.Add(ExtensionMethods.CreateProperty("Invisible Rail Settings/Start Cap", Variant.Type.NodePath, PropertyHint.NodePathValidTypes, "Node3D"));
				properties.Add(ExtensionMethods.CreateProperty("Invisible Rail Settings/End Cap", Variant.Type.NodePath, PropertyHint.NodePathValidTypes, "Node3D"));
				properties.Add(ExtensionMethods.CreateProperty("Invisible Rail Settings/Collider", Variant.Type.NodePath, PropertyHint.NodePathValidTypes, "CollisionShape3D"));
			}

			return properties;
		}

		public override Variant _Get(StringName property)
		{
			switch ((string)property)
			{
				case "Rail Path":
					return railPath;

				case "Invisible Rail Settings/Enabled":
					return isInvisibleRail;
				case "Invisible Rail Settings/Length":
					return RailLength;

				case "Invisible Rail Settings/Rail Object":
					return railModelPath;
				case "Invisible Rail Settings/Rail Material":
					return railMaterial;

				case "Invisible Rail Settings/Start Cap":
					return startCapPath;
				case "Invisible Rail Settings/End Cap":
					return endCapPath;
				case "Invisible Rail Settings/Collider":
					return colliderPath;
			}

			return base._Get(property);
		}

		public override bool _Set(StringName property, Variant value)
		{
			switch ((string)property)
			{
				case "Rail Path":
					railPath = (NodePath)value;
					break;

				case "Invisible Rail Settings/Enabled":
					isInvisibleRail = (bool)value;
					NotifyPropertyListChanged();
					break;
				case "Invisible Rail Settings/Length":
					RailLength = (int)value;
					break;


				case "Invisible Rail Settings/Rail Object":
					railModelPath = (NodePath)value;
					break;
				case "Invisible Rail Settings/Rail Material":
					railMaterial = (Material)value;
					break;

				case "Invisible Rail Settings/Start Cap":
					startCapPath = (NodePath)value;
					break;
				case "Invisible Rail Settings/End Cap":
					endCapPath = (NodePath)value;
					break;
				case "Invisible Rail Settings/Collider":
					colliderPath = (NodePath)value;
					break;
				default:
					return false;
			}

			return true;
		}
		#endregion

		[Signal]
		public delegate void GrindStartedEventHandler();
		[Signal]
		public delegate void GrindCompletedEventHandler();

		private Path3D rail;
		private NodePath railPath;
		private PathFollow3D pathFollower;

		private NodePath colliderPath;
		private NodePath railModelPath;
		private NodePath startCapPath;
		private NodePath endCapPath;

		private Node3D railModel;
		private Material railMaterial;
		private Node3D startCap;
		private Node3D endCap;
		private CollisionShape3D collider;
		public int RailLength { get; set; } //Only used on invisible rails
		private bool isInvisibleRail;

		[Export]
		private AudioStreamPlayer sfx;
		private bool isFadingSFX;

		private bool isActive; //Is the rail active?
		private bool isInteractingWithPlayer; //Check for collisions?

		private CameraController Camera => CameraController.instance;
		private CharacterController Character => CharacterController.instance;
		private CharacterSkillManager Skills => Character.Skills;
		private InputManager.Controller Controller => InputManager.controller;

		private readonly float GRIND_STEP_HEIGHT = 1.6f; //How high to jump during a grindstep
		private readonly float GRIND_STEP_SPEED = 24.0f; //How fast to move during a grindstep

		public override void _Ready()
		{
			if (Engine.IsEditorHint())
				return;

			pathFollower = new PathFollow3D()
			{
				Loop = false,
				CubicInterp = false,
				RotationMode = PathFollow3D.RotationModeEnum.Oriented
			};

			rail = GetNode<Path3D>(railPath);
			rail.CallDeferred("add_child", pathFollower);

			if (isInvisibleRail) //For Secret Rings' hidden rails
			{
				UpdateInvisibleRailLength();

				collider = GetNode<CollisionShape3D>(colliderPath);
				railModel = GetNode<Node3D>(railModelPath);

				railModel.Visible = false;

				//Generate curve and collision
				collider.Shape = new BoxShape3D()
				{
					Size = new Vector3(.5f, .5f, RailLength)
				};
				collider.Position = Vector3.Forward * RailLength * .5f + Vector3.Down * .05f;
				rail.Curve = new Curve3D();
				rail.Curve.AddPoint(Vector3.Zero, null, Vector3.Forward);
				rail.Curve.AddPoint(Vector3.Forward * RailLength, Vector3.Back);
			}
		}

		public override void _PhysicsProcess(double _)
		{
			if (Engine.IsEditorHint())
			{
				UpdateInvisibleRailLength();
				return;
			}

			if (isFadingSFX)
				isFadingSFX = SoundManager.instance.FadeSFX(sfx);

			if (isActive)
				UpdateRail();
			else if (isInteractingWithPlayer)
				CheckRailActivation();
		}

		/// <summary> Timer to keep track of shuffles. </summary>
		private float shuffleTimer;
		/// <summary> Is a shuffle currently being buffered? </summary>
		private float shuffleBufferTimer;
		private const float SHUFFLE_BUFFER_LENGTH = .2f;
		/// <summary> How long a shuffle takes. </summary>
		private const float GRIND_RAIL_SHUFFLE_LENGTH = .5f;
		/// <summary> How "magnetic" the rail is. Early 3D Sonic games had a habit of putting this too low. </summary>
		private const float GRIND_RAIL_SNAPPING = .5f;
		/// <summary> Rail snapping is more generous when performing a grind step. </summary>
		private const float GRINDSTEP_RAIL_SNAPPING = 1.2f;
		private Vector3 closestPoint;
		private void CheckRailActivation()
		{
			if (Character.IsOnGround && !Character.JustLandedOnGround) return; //Can't start grinding from the ground
			if (Character.Lockon.IsHomingAttacking) return; //Character is targeting something
			if (Character.MovementState != CharacterController.MovementStates.Normal) return; //Character is busy

			//Sync rail pathfollower
			Vector3 delta = rail.GetLocalPosition(Character.GlobalPosition);
			pathFollower.Progress = rail.Curve.GetClosestOffset(delta);

			//Check walls
			if (CheckWall(Skills.grindSettings.speed * PhysicsManager.physicsDelta)) return;

			float horizontalOffset = Mathf.Abs(pathFollower.GetLocalPosition(Character.GlobalPosition).x); //Get local offset

			if (Character.VerticalSpd <= 0f)
			{
				if (horizontalOffset < GRIND_RAIL_SNAPPING ||
					(Character.IsGrindstepJump && horizontalOffset < GRINDSTEP_RAIL_SNAPPING)) //Start grinding
					ActivateRail();
			}
		}

		private void ActivateRail()
		{
			isActive = true;
			Character.IsGrindstepJump = false; //Reset grind step

			isFadingSFX = false;
			sfx.VolumeDb = 0f; //Reset volume
			sfx.Play();

			if (isInvisibleRail)
			{
				railModel.Visible = true;
				UpdateInvisibleRailPosition();
			}

			shuffleBufferTimer = 0; //Reset buffer timer
			shuffleTimer = GRIND_RAIL_SHUFFLE_LENGTH; //Character starts with a shuffle

			Character.ResetActionState(); //Cancel stomps, jumps, etc
			Character.StartExternal(this, pathFollower);

			Character.IsMovingBackward = false;
			Character.LandOnGround(); //Rail counts as being on the ground
			Character.MoveSpeed = Skills.grindSettings.speed; //Start at the correct speed
			Character.VerticalSpd = 0f;

			Character.Animator.ExternalAngle = 0; //Rail modifies Character's Transform directly, animator angle is unused.
			Character.Animator.StartBalancing();
			Character.Animator.SnapRotation(Character.Animator.ExternalAngle); //Snap

			Character.Connect(CharacterController.SignalName.Damaged, new Callable(this, MethodName.DisconnectFromRail));
			Character.Connect(CharacterController.SignalName.ExternalControlCompleted, new Callable(this, MethodName.DisconnectFromRail));

			EmitSignal(SignalName.GrindStarted);
		}

		private void UpdateRail()
		{
			if (Character.MovementState != CharacterController.MovementStates.External ||
				Character.ExternalController != this) //Player must have disconnected from the rail
			{
				DisconnectFromRail();
				return;
			}

			float railAngle = Character.CalculateForwardAngle(pathFollower.Forward());

			//Delta angle to rail's movement direction (NOTE - Due to Godot conventions, negative is right, positive is left)
			if (Controller.jumpButton.wasPressed)
			{
				DisconnectFromRail();

				//Check if the player is holding a direction parallel to rail.
				Character.IsGrindstepJump = Character.IsHoldingDirection(railAngle + Mathf.Pi * .5f) ||
				 Character.IsHoldingDirection(railAngle - Mathf.Pi * .5f);
				if (Character.IsGrindstepJump) //Grindstep
				{
					float inputDeltaAngle = ExtensionMethods.SignedDeltaAngleRad(Character.GetTargetInputAngle(), railAngle);
					//Calculate how far player is trying to go
					float horizontalTarget = GRIND_STEP_SPEED * Mathf.Sign(inputDeltaAngle);
					horizontalTarget *= Mathf.SmoothStep(0.5f, 1f, Controller.MovementAxisLength); //Give some smoothing based on controller strength

					Character.MovementAngle += Mathf.Pi * .5f * Mathf.Sign(inputDeltaAngle);
					Character.VerticalSpd = RuntimeConstants.GetJumpPower(GRIND_STEP_HEIGHT);
					Character.MoveSpeed = new Vector2(horizontalTarget, Character.MoveSpeed).Length();

					Character.IsOnGround = false; //Disconnect from the ground
					Character.CanJumpDash = false; //Disable jumpdashing

					Character.Animator.StartGrindStep();
				}
				else //Jump normally
					Character.Jump(true);

				return;
			}

			Character.UpDirection = pathFollower.Up();
			Character.MovementAngle = railAngle;
			Character.MoveSpeed = Skills.grindSettings.Interpolate(Character.MoveSpeed, 0f); //Slow down due to friction
			Character.CheckCeiling(); //Check for crushers

			//Update shuffling
			if (Controller.actionButton.wasPressed)
			{
				if (Mathf.IsZeroApprox(shuffleTimer))
					StartShuffle(false); //Mistimed shuffle
				else if (Mathf.IsZeroApprox(shuffleBufferTimer))
					shuffleBufferTimer = SHUFFLE_BUFFER_LENGTH;
				else //Don't allow button mashers
					shuffleBufferTimer = -SHUFFLE_BUFFER_LENGTH;
			}

			if (shuffleTimer > 0)
			{
				shuffleTimer = Mathf.MoveToward(shuffleTimer, 0, PhysicsManager.physicsDelta);
				shuffleBufferTimer = Mathf.MoveToward(shuffleBufferTimer, 0, PhysicsManager.physicsDelta);

				if (Mathf.IsZeroApprox(shuffleBufferTimer))
				{
					if (shuffleBufferTimer > 0)
						StartShuffle(true);

					shuffleBufferTimer = 0;
				}
			}

			if (isInvisibleRail)
				UpdateInvisibleRailPosition();

			//Check wall
			float movementDelta = Character.MoveSpeed * PhysicsManager.physicsDelta;
			RaycastHit hit = CheckWall(movementDelta);
			if (hit && hit.collidedObject is StaticBody3D) //Stop player when colliding with a static body
			{
				movementDelta = hit.distance; //Limit movement distance
				Character.MoveSpeed = 0f;
			}

			pathFollower.Progress += movementDelta;
			Character.UpdateExternalControl();
			Character.Animator.UpdateBalancing();

			if (pathFollower.ProgressRatio >= 1 || Mathf.IsZeroApprox(Character.MoveSpeed)) //Disconnect from the rail
				DisconnectFromRail();
		}

		private void StartShuffle(bool isPerfectShuffle)
		{
			shuffleTimer = GRIND_RAIL_SHUFFLE_LENGTH;

			sfx.Play();

			Character.MoveSpeed = isPerfectShuffle ? Skills.perfectShuffleSpeed : Skills.grindSettings.speed;
			Character.Animator.StartGrindShuffle();
		}

		private void DisconnectFromRail()
		{
			if (!isActive) return;

			isFadingSFX = true; //Start fading sound effect

			isActive = false;
			isInteractingWithPlayer = false;
			if (isInvisibleRail)
				railModel.Visible = false;

			Character.ResetMovementState();
			Character.Animator.ResetState(.1f);
			Character.Animator.SnapRotation(Character.MovementAngle);

			//Disconnect signals
			if (Character.IsConnected(CharacterController.SignalName.Damaged, new Callable(this, MethodName.DisconnectFromRail)))
				Character.Disconnect(CharacterController.SignalName.Damaged, new Callable(this, MethodName.DisconnectFromRail));
			if (Character.IsConnected(CharacterController.SignalName.ExternalControlCompleted, new Callable(this, MethodName.DisconnectFromRail)))
				Character.Disconnect(CharacterController.SignalName.ExternalControlCompleted, new Callable(this, MethodName.DisconnectFromRail));

			EmitSignal(SignalName.GrindCompleted);
		}

		private RaycastHit CheckWall(float movementDelta)
		{
			float castLength = movementDelta + Character.CollisionRadius;
			RaycastHit hit = this.CastRay(pathFollower.GlobalPosition, pathFollower.Forward() * castLength, Character.CollisionMask);
			Debug.DrawRay(pathFollower.GlobalPosition, pathFollower.Forward() * castLength, hit ? Colors.Red : Colors.White);
			return hit;
		}

		private void UpdateInvisibleRailPosition()
		{
			railModel.GlobalPosition = Character.GlobalPosition;
			railModel.Position = new Vector3(0, railModel.Position.y, railModel.Position.z); //Ignore player's x-offset
			railMaterial.Set("uv_offset", railModel.Position.z % 1);
		}

		private void UpdateInvisibleRailLength()
		{
			startCap = GetNodeOrNull<Node3D>(startCapPath);
			endCap = GetNodeOrNull<Node3D>(endCapPath);

			if (startCap != null)
				startCap.Position = Vector3.Forward;

			if (endCap != null)
				endCap.Position = Vector3.Forward * (RailLength - 1);
		}

		public void OnEntered(Area3D a)
		{
			if (!a.IsInGroup("player")) return;

			isInteractingWithPlayer = true;
			CheckRailActivation();
		}

		public void OnExited(Area3D a)
		{
			if (!a.IsInGroup("player")) return;
			isInteractingWithPlayer = false;

			DisconnectFromRail();
		}
	}
}
