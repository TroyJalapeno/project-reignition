using Godot;
using Godot.Collections;
using Project.Core;

namespace Project.Gameplay.Bosses
{
	public partial class Erazor : Node3D
	{
		[Export]
		private int health;
		private int damageTaken;

		[Export]
		private Area3D lockonTarget;

		[Export]
		private Array<BossPatternResource> patterns;
		public static int CurrentPattern { get; private set; }

		[Export]
		private AnimationTree animator;

		private float timer;
		private int currentAttackIndex;
		private ErazorAttack CurrentAttack => attacks[currentAttackIndex];
		private readonly Array<ErazorAttack> attacks = new Array<ErazorAttack>();

		private bool isTeleporting;
		private bool isNearPlayer = true; //Keeps track of whether Erazor is near the player or not

		private float targetStrafe; //Left and right movement
		private float currentStrafe; //Smoothed value
		private float currentStrafeVelocity; //Smoothed value
		private float distanceVelocity;

		[Export]
		private AnimationPlayer duelAnimator;
		private bool duelCharged; //Ready to strike?

		private float currentDistance = 30f; //Current distance from the player
		private const float IDLE_DISTANCE = 20f; //How much distance to keep normally
		private const float ATTACK_DISTANCE = 10f; //How much distance to have when attacking
		private const float STRAFE_SMOOTHING = .2f;

		[Export]
		private CameraSettingsResource standardCamera;
		[Export]
		private CameraSettingsResource duelCamera; //Sideview camera used for duels
		private bool IsDueling => CurrentAttack != null && CurrentAttack.AttackType == 4;
		private const float DUEL_SMOOTHING = .4f;
		private const float MAX_DUEL_SPEED = 80f;
		private const float DUEL_DISTANCE = 50f; //How much distance to have when dueling
		private const float DUEL_DIALOG_DELAY = 4f; //How long to wait before starting hint dialog
		[Signal]
		public delegate void DuelCompletedEventHandler(); //Called when a duel attack ends. Resets positions to allow infinte hallway

		private CharacterController Character => CharacterController.instance;
		private float LocalPlayerPosition => Character.PathFollower.LocalPlayerPositionDelta.X;

		//Animation parameters
		private const string TELEPORT_SPEED = "parameters/TeleportSpeed/scale";
		private const string TELEPORTING_PARAMETER = "parameters/Teleporting/current";
		private const string ATTACK_TYPE_PARAMETER = "parameters/StartType/current";
		private const string WINDUP_TYPE_PARAMETER = "parameters/WindupType/current";
		private const string STRIKE_TYPE_PARAMETER = "parameters/StrikeType/current";
		private const string CURRENT_ACTION_PARAMETER = "parameters/CurrentAction/current";

		public override void _Ready()
		{
			animator.Active = true;

			CurrentPattern = 0; //Reset pattern
			LoadAttackPattern();

			Character.Camera.UpdateCameraSettings(new CameraBlendData()
			{
				SettingsResource = standardCamera,
			});
			StartTeleportation();
		}

		public override void _PhysicsProcess(double _)
		{
			int actionState = (int)animator.Get(CURRENT_ACTION_PARAMETER);

			if (isTeleporting) //Process teleporation
			{
				actionState = (int)animator.Get(TELEPORTING_PARAMETER);

				if (actionState == 2) //Waiting
				{
					isNearPlayer = !isNearPlayer;
					targetStrafe = currentStrafe = currentStrafeVelocity = 0; //Reset strafe
					currentDistance = isNearPlayer ? (IsDueling ? DUEL_DISTANCE : ATTACK_DISTANCE) : IDLE_DISTANCE;
					animator.Set(TELEPORTING_PARAMETER, 3); //Finish Teleportation
				}
				else if (actionState == 0) //Finished teleportation
					isTeleporting = false;
			}
			else if (actionState == 0) //Idling
			{
				timer += PhysicsManager.physicsDelta;

				if (isNearPlayer) //Exception - Duel attacks take place from a distance.
				{
					if (timer >= CurrentAttack.Delay)
						StartAttack();
				}
				else if (timer >= CurrentAttack.TeleportDelay)
					StartTeleportation();
			}
			else if (actionState == 4) //Attack Finished
			{
				animator.Set(CURRENT_ACTION_PARAMETER, 0);
				//Queue the next attack
				currentAttackIndex++;
				if (currentAttackIndex >= attacks.Count) //Loop attacks
					currentAttackIndex = 0;

				if (CurrentAttack.TeleportDelay != 0) //Teleport away
					StartTeleportation();
			}
			else
			{
				bool isAttacking = actionState == 3;

				//Track player?
				if (!isAttacking)
				{
					if (CurrentAttack.AttackType == 0) //"I" attack
					{
						if (timer < CurrentAttack.Startup - .1f)
							targetStrafe = LocalPlayerPosition;
					}
					else if (CurrentAttack.AttackType == 1) //"V" attack
						targetStrafe = Mathf.Clamp(LocalPlayerPosition, -3f, 3f);
					else if (CurrentAttack.AttackType == 2) //"L" attack
						targetStrafe = 3f;

					if (actionState == 2) //Windup
					{
						if (IsDueling) //Custom logic for duels
						{
							if (duelCharged) //Move
							{
								if (SoundManager.instance.IsDialogActive) //Hint is active
									timer = DUEL_DIALOG_DELAY;
								else if (timer >= DUEL_DIALOG_DELAY + .4f) //Extra anticipation after hint disappears
								{
									//Update Camera
									duelCamera.distance = Mathf.Lerp(15, 35, Mathf.Clamp(currentDistance / DUEL_DISTANCE, 0f, 1f));

									currentDistance = ExtensionMethods.SmoothDamp(currentDistance, -20f, ref distanceVelocity, DUEL_SMOOTHING, MAX_DUEL_SPEED);
									if (Character.Lockon.IsHomingAttacking)
										currentDistance -= Character.Skills.homingAttackSpeed * PhysicsManager.physicsDelta;

									if (currentDistance <= 0)
									{
										duelAnimator.Seek(0, true);
										duelAnimator.Play("activate");

										if (Character.Lockon.IsHomingAttacking)
										{
											//Take Damage
										}
										else
										{
											//Deal Damage
										}
									}
								}
							}
							else if (timer >= DUEL_DIALOG_DELAY) //Hint
							{
								duelCharged = true;
								/*
								TODO Play anticipation sfx
								StageSettings.instance.dialogLibrary.GetStream("");
								*/
							}
						}
						else if (timer >= CurrentAttack.Startup)
						{
							timer = 0;
							animator.Set(CURRENT_ACTION_PARAMETER, 3); //Strike
						}

						timer += PhysicsManager.physicsDelta;
					}
				}
				else
				{
					timer += PhysicsManager.physicsDelta;
					if (CurrentAttack.AttackType == 2 && timer > .4f) //"L" attack, cont.
					{
						targetStrafe = 0f;
						currentStrafe = ExtensionMethods.SmoothDamp(currentStrafe, targetStrafe, ref currentStrafeVelocity, STRAFE_SMOOTHING);
					}
				}
			}

			//Face Path3D
			GlobalRotation = Character.PathFollower.GlobalRotation;
			RotateY(Mathf.Pi);

			//Update Position
			GlobalPosition = Character.PathFollower.GlobalPosition + Character.PathFollower.Forward() * currentDistance;
			currentStrafe = ExtensionMethods.SmoothDamp(currentStrafe, targetStrafe, ref currentStrafeVelocity, STRAFE_SMOOTHING);
			GlobalPosition += this.Left() * currentStrafe;
		}

		private void StartAttack()
		{
			timer = 0;
			animator.Set(ATTACK_TYPE_PARAMETER, attacks[currentAttackIndex].AttackType);
			animator.Set(WINDUP_TYPE_PARAMETER, attacks[currentAttackIndex].AttackType);
			animator.Set(STRIKE_TYPE_PARAMETER, attacks[currentAttackIndex].AttackType);
			animator.Set(CURRENT_ACTION_PARAMETER, 1); //Start Attack

			if (IsDueling)
				StartDuel();
		}

		private void StartDuel()
		{
			duelAnimator.Seek(0, true);
			duelAnimator.Play("activate");
			duelCharged = false;
			isNearPlayer = true;
			distanceVelocity = 0;

			//TODO Play audio

			//Update camera
			duelCamera.distance = 35f;
			Character.Camera.UpdateCameraSettings(new CameraBlendData()
			{
				SettingsResource = duelCamera
			});

			//TODO Toggle sidescrolling
		}

		//Move everything back to the start and finish transition
		private void FinishDuel()
		{
			EmitSignal(SignalName.DuelCompleted);
		}

		private void StartTeleportation()
		{
			isTeleporting = true;
			animator.Set(TELEPORTING_PARAMETER, 1); //Start teleportation
		}

		private void TakeDamage(int amount)
		{
			damageTaken += amount;

			if (damageTaken >= health)
			{
				//Defeat boss
			}
			else if (CurrentPattern < patterns.Count - 1 && damageTaken > patterns[CurrentPattern].damage) //Advance attack pattern
			{
				CurrentPattern++;
				LoadAttackPattern();
			}
		}

		private void LoadAttackPattern()
		{
			attacks.Clear();
			currentAttackIndex = 0; //Reset current attack

			string[] data = patterns[CurrentPattern].attacks.Split('\n');

			for (int i = 0; i < data.Length; i++)
			{
				ErazorAttack a = ProcessAttack(data[i]);
				if (a.AttackType != -1) //Valid attack
					attacks.Add(a);
			}
		}

		private ErazorAttack ProcessAttack(string attackData)
		{
			ErazorAttack attack = new ErazorAttack()
			{
				AttackType = -1,
				Delay = 0,
				Startup = 0,
				TeleportDelay = 0,
			};

			string[] parameters = attackData.Split(',');
			for (int i = 0; i < parameters.Length; i++)
			{
				string[] values = parameters[i].Split("->");

				if (values[0].Equals("type"))
					attack.AttackType = GetAttackType(values[1]);
				else if (values[0].Equals("delay"))
					attack.Delay = values[1].ToFloat();
				else if (values[0].Equals("startup"))
					attack.Startup = values[1].ToFloat();
				else if (values[0].Equals("teleport"))
					attack.TeleportDelay = values[1].ToFloat();
			}

			return attack;
		}

		private int GetAttackType(string type)
		{
			switch (type)
			{
				case "I":
					return 0;
				case "V":
					return 1;
				case "L":
					return 2;
				case "Z":
					return 3;
				case "D":
					return 4;
			}

			return -1;
		}

		private partial class ErazorAttack : GodotObject
		{
			public int AttackType { get; set; } //What kind of attack is it?
			public float Delay { get; set; } //How long before the attack animation actually starts?
			public float Startup { get; set; } //How long should the aniticipation be?
			public float TeleportDelay { get; set; } //Set to zero to skip teleporation
		}
	}
}
