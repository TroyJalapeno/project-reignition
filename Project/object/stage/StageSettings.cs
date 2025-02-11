using System.Collections.Generic;
using Godot;
using Godot.Collections;
using Project.Core;

namespace Project.Gameplay
{
	/// <summary>
	/// Stage settings.
	/// Must be the first thing loaded in a level.
	/// </summary>
	public partial class StageSettings : Node3D
	{
		public static StageSettings instance;

		public bool IsControlTest => Data.LevelID == OPTIONS_LEVEL_ID;
		private readonly StringName OPTIONS_LEVEL_ID = "options";

		public override void _EnterTree()
		{
			instance = this; // Always override previous instance

			for (int i = 0; i < pathParent.GetChildCount(); i++)
			{
				Path3D path = pathParent.GetChildOrNull<Path3D>(i);
				if (path != null)
					pathList.Add(path);
			}

			CalculateTechnicalBonus();
			UpdateScore(0, MathModeEnum.Replace);
			UpdatePostProcessingStatus();

			if (IsControlTest)
				LevelState = LevelStateEnum.Ingame;
			else
				LevelState = LevelStateEnum.Loading;

			StartingSaturation = Environment.Environment.AdjustmentSaturation;
		}


		public override void _Ready()
		{
			if (IsControlTest)
				return;

			// Fixes obnoxious flickering when testing from the editor
			if (OS.IsDebugBuild() && !TransitionManager.IsTransitionActive)
			{
				TransitionManager.StartTransition(new()
				{
					outSpeed = .5f,
					color = Colors.Black,
					disableAutoTransition = true,
				});
			}

			InitializeShaders();
		}


		public void UpdatePostProcessingStatus()
		{
			bool postProcessingEnabled = SaveManager.Config.postProcessingQuality != SaveManager.QualitySetting.Disabled;
			Environment.Environment.SsaoEnabled = postProcessingEnabled;
			Environment.Environment.SsilEnabled = postProcessingEnabled;
		}


		/// <summary> Gets ALL the materials in this stage, then compiles them. </summary>
		public void InitializeShaders()
		{
			if (OS.IsDebugBuild() && !DebugManager.Instance.IsShaderCompilationEnabled)
				return;

			foreach (Node node in GetChildren(GetTree().Root, new List<Node>()))
			{
				if (node is MeshInstance3D)
				{
					ShaderManager.Instance.QueueMesh((node as MeshInstance3D).Mesh);
					continue;
				}

				if (node is GpuParticles3D)
				{
					GpuParticles3D particles = node as GpuParticles3D;
					ShaderManager.Instance.QueueMaterial(particles.ProcessMaterial);
					ShaderManager.Instance.QueueMesh(particles.DrawPass1);
					continue;
				}
			}

			ShaderManager.Instance.StartCompilation();
		}


		private List<Node> GetChildren(Node parent, List<Node> nodes)
		{
			nodes.Add(parent);
			foreach (Node child in parent.GetChildren())
				nodes = GetChildren(child, nodes);
			return nodes;
		}


		[Signal]
		public delegate void ReflectionProbesCalculatedEventHandler();
		private int probeFrameCounter;
		private const int PROBE_FRAME_COUNT_LENGTH = 90;
		public override void _Process(double _)
		{
			/*
			TODO 
			Temporary workaround because reflection probes are slow.
			Remove this when Godot adds a way to do quick "UPDATE_ONCE" reflection probes
			*/

			if (LevelState == LevelStateEnum.Loading)
			{
				probeFrameCounter++;

				if (probeFrameCounter >= PROBE_FRAME_COUNT_LENGTH && !ShaderManager.Instance.IsCompilingShaders)
				{
					LevelState = LevelStateEnum.Ingame;
					TransitionManager.FinishTransition();
					EmitSignal(SignalName.ReflectionProbesCalculated);
				}

				return;
			}

			UpdateTime();
		}


		#region Level Settings
		/// <summary> Reference to the level's data. </summary>
		[Export]
		public LevelDataResource Data { get; private set; }
		[Export]
		public CameraSettingsResource InitialCameraSettings { get; private set; }
		[Export]
		public SFXLibraryResource dialogLibrary;

		/// <summary>
		/// Calculates the rank [Fail = -1, None = 0, Bronze = 1, Silver = 2, Gold = 3]
		/// </summary>
		public int CalculateRank()
		{
			if (LevelState == LevelStateEnum.Failed)
				return -1;

			int rank = 0; // DEFAULT - No rank

			if (Data.SkipScore)
			{
				if (CurrentTime <= Data.GoldTime)
					rank = 3;
				else if (CurrentTime <= Data.SilverTime)
					rank = 2;
				else if (CurrentTime <= Data.BronzeTime)
					rank = 1;
			}
			else
			{
				int score = TotalScore;
				if (CurrentTime <= Data.GoldTime && score >= Data.Score) // Perfect run
					rank = 3;
				else if (CurrentTime <= Data.SilverTime && score >= 3 * (Data.Score / 4)) // Silver score reqs are always 3/4 of gold
					rank = 2;
				else if (CurrentTime <= Data.BronzeTime) // Bronze is easy to get
					rank = 1;
			}

			if (rank >= 3 && RespawnCount != 0) // Limit to silver if a respawn occured
				rank = 2;

			return rank;
		}
		#endregion


		#region Level Data
		public enum MathModeEnum // List of ways the score can be modified
		{
			Add,
			Subtract,
			Multiply,
			Replace
		}
		/// <summary>
		/// Calculates value based on provided MathMode.
		/// </summary>
		private int CalculateMath(int value, int amount, MathModeEnum mode)
		{
			switch (mode)
			{
				case MathModeEnum.Add:
					value += amount;
					break;
				case MathModeEnum.Subtract:
					value -= amount;
					if (value < 0) // Clamp to zero
						value = 0;
					break;
				case MathModeEnum.Multiply:
					value *= amount;
					break;
				case MathModeEnum.Replace:
					value = amount;
					break;
			}
			return value;
		}

		[Signal]
		public delegate void ScoreChangedEventHandler(); // Score has changed, normally occours from a bonus
		public int CurrentScore { get; private set; } // How high is the current score?
		public string DisplayScore { get; private set; } // Current score formatted to eight zeros
		/// <summary> Total score, including ring and technical bonus. </summary>
		public int TotalScore => CurrentScore + Mathf.CeilToInt(RingBonus * TechnicalBonus);
		public void UpdateScore(int amount, MathModeEnum mode)
		{
			CurrentScore = CalculateMath(CurrentScore, amount, mode);
			DisplayScore = ExtensionMethods.FormatMenuNumber(CurrentScore);
			EmitSignal(SignalName.ScoreChanged);
		}

		/// <summary> How many times has the player taken damage? </summary>
		public int DamageCount { get; private set; }
		/// <summary> The number of times the player has respawned. </summary>
		public int RespawnCount { get; private set; } // How high many times did the player have to respawn?
		public void IncrementDamageCount()
		{
			DamageCount++;
			CalculateTechnicalBonus();
		}
		public void IncrementRespawnCount()
		{
			RespawnCount++;
			CalculateTechnicalBonus();
		}
		public float TechnicalBonus { get; private set; }

		private void CalculateTechnicalBonus()
		{
			if (LevelState == LevelStateEnum.Failed)
			{
				// Failing the level gives a technical bonus of .5
				TechnicalBonus = .5f;
				return;
			}

			if (RespawnCount != 0 || DamageCount >= 6)
			{
				// Respawning automatically means 1.0
				TechnicalBonus = 1.0f;
				return;
			}

			// Damage values
			if (DamageCount >= 4)
			{
				// 4-5
				TechnicalBonus = 1.1f;
				return;
			}

			if (DamageCount >= 2)
			{
				// 2-3
				TechnicalBonus = 1.2f;
				return;
			}

			if (DamageCount == 1)
			{
				// 1
				TechnicalBonus = 1.5f;
				return;
			}

			TechnicalBonus = 2.0f; // Perfect run
		}

		//Objectives
		public int CurrentObjectiveCount { get; private set; } // How much has the player currently completed?
		[Signal]
		public delegate void ObjectiveChangedEventHandler(); // Progress towards the objective has changed
		public void IncrementObjective()
		{
			CurrentObjectiveCount++;
			EmitSignal(SignalName.ObjectiveChanged);

			if (Data.MissionObjectiveCount == 0) // i.e. Sand Oasis's "Don't break the jars!" mission.
				FinishLevel(false);
			else if (CurrentObjectiveCount >= Data.MissionObjectiveCount)
				FinishLevel(true);
		}

		// Rings
		public int CurrentRingCount { get; private set; } // How many rings is the player currently holding?
		public int RingBonus { get; private set; }
		[Signal]
		public delegate void RingChangedEventHandler(int change); // Ring count has changed
		public void UpdateRingCount(int amount, MathModeEnum mode, bool disableAnimations = false)
		{
			int previousAmount = CurrentRingCount;
			CurrentRingCount = CalculateMath(CurrentRingCount, amount, mode);
			RingBonus = CurrentRingCount * 10;
			if (Data.MissionType == LevelDataResource.MissionTypes.Ring && CurrentRingCount >= Data.MissionObjectiveCount) // For ring based missions
			{
				CurrentRingCount = Data.MissionObjectiveCount; // Clamp
				FinishLevel(true);
			}

			if (DebugManager.Instance.InfiniteRings) // Infinite ring cheat
				CurrentRingCount = 999;

			EmitSignal(SignalName.RingChanged, CurrentRingCount - previousAmount, disableAnimations);
		}


		public int CurrentEXP { get; private set; } // How much exp is the player earning from this stage?

		// Time
		[Signal]
		public delegate void TimeChangedEventHandler(); // Time has changed.

		public float CurrentTime { get; private set; } // How long has the player been on this level? (In Seconds)
		public string DisplayTime { get; private set; } // Current time formatted in mm:ss.ff
		private void UpdateTime()
		{
			if (!IsLevelIngame || !Interface.PauseMenu.AllowPausing) return;

			CurrentTime += PhysicsManager.normalDelta; // Add current time
			DisplayTime = ExtensionMethods.FormatTime(CurrentTime);
			if (Data.MissionTimeLimit != 0 && CurrentTime >= Data.MissionTimeLimit) // Time's up!
				FinishLevel(false);

			EmitSignal(SignalName.TimeChanged);
		}
		#endregion


		#region Path Settings
		[Export(PropertyHint.NodeType, "Node3D")]
		private Node3D pathParent;
		/// <summary> List of all level paths contained for this level. </summary>
		private readonly Array<Path3D> pathList = new();

		/// <summary>
		/// Returns the path the player is currently the closest to.
		/// Allows placing the player anywhere in the editor without needing to manually assign paths.
		/// </summary>
		public Path3D CalculateStartingPath(Vector3 globalPosition)
		{
			int closestPathIndex = -1;
			float closestDistanceSquared = Mathf.Inf;

			for (int i = 0; i < pathList.Count; i++)
			{
				Vector3 closestPoint = pathList[i].Curve.GetClosestPoint(globalPosition - pathList[i].GlobalPosition);
				closestPoint += pathList[i].GlobalPosition;
				float dstSquared = globalPosition.DistanceSquaredTo(closestPoint);

				if (dstSquared < closestDistanceSquared)
				{
					closestPathIndex = i;
					closestDistanceSquared = dstSquared;
				}
			}

			if (closestPathIndex == -1)
				return null;

			return pathList[closestPathIndex];
		}
		#endregion


		#region Object Spawning
		// Checkpoint data
		[Signal]
		public delegate void TriggeredCheckpointEventHandler();
		public Triggers.CheckpointTrigger CurrentCheckpoint { get; private set; }
		public void SetCheckpoint(Triggers.CheckpointTrigger checkpoint)
		{
			if (checkpoint == CurrentCheckpoint) return; // Already at this checkpoint

			CurrentCheckpoint = checkpoint;
			checkpoint.UpdateCheckpointData();
			EmitSignal(SignalName.TriggeredCheckpoint);
		}


		[Signal]
		public delegate void UnloadedEventHandler();
		private const string UNLOAD_FUNCTION = "Unload"; // Clean up any memory leaks in this function
		public override void _ExitTree() => EmitSignal(SignalName.Unloaded);
		public void ConnectUnloadSignal(Node node)
		{
			if (!node.HasMethod(UNLOAD_FUNCTION))
			{
				GD.PrintErr($"Node {node.Name} doesn't have a function '{UNLOAD_FUNCTION}!'");
				return;
			}

			if (!IsConnected(SignalName.Unloaded, new Callable(node, UNLOAD_FUNCTION)))
				Connect(SignalName.Unloaded, new Callable(node, UNLOAD_FUNCTION));
		}


		[Signal]
		public delegate void RespawnedEventHandler();
		public readonly static StringName RESPAWN_FUNCTION = "Respawn"; // Default name of respawn functions
		public void ConnectRespawnSignal(Node node)
		{
			if (!node.HasMethod(RESPAWN_FUNCTION))
			{
				GD.PrintErr($"Node {node.Name} doesn't have a function '{RESPAWN_FUNCTION}!'");
				return;
			}

			if (!IsConnected(SignalName.Respawned, new Callable(node, RESPAWN_FUNCTION)))
				Connect(SignalName.Respawned, new Callable(node, RESPAWN_FUNCTION), (uint)ConnectFlags.Deferred);
		}


		public void RespawnObjects()
		{
			SoundManager.instance.CancelDialog(); // Cancel any active dialog
			EmitSignal(SignalName.Respawned);
		}

		#endregion


		#region Level Completion
		[Signal]
		public delegate void LevelCompletedEventHandler(); // Called when the level is completed
		[Signal]
		public delegate void LevelDemoStartedEventHandler(); // Called when the level demo starts

		public enum LevelStateEnum
		{
			Loading, // TODO Delete this when Godot fixes reflection probes
			Ingame,
			Failed,
			Success,
		}
		public LevelStateEnum LevelState { get; private set; }
		public bool IsLevelIngame => LevelState == LevelStateEnum.Ingame;
		private const float FAIL_COMPLETION_DELAY = 1.5f; // Mission fails always have a delay of 1.5 seconds
		public void FinishLevel(bool wasSuccessful)
		{
			if (!IsLevelIngame)
				return;

			// Recalculate technical bonus
			CalculateTechnicalBonus();

			// Attempt to start the completion demo
			GetTree().CreateTimer(wasSuccessful ? Data.CompletionDelay : FAIL_COMPLETION_DELAY).Connect(SceneTreeTimer.SignalName.Timeout, new Callable(this, MethodName.StartCompletionDemo));

			BGMPlayer.StageMusicPaused = true;
			Interface.PauseMenu.AllowPausing = false;
			LevelState = wasSuccessful ? LevelStateEnum.Success : LevelStateEnum.Failed;

			EmitSignal(SignalName.LevelCompleted);
		}

		/// <summary> Camera demo that gets enabled after the level is cleared. </summary>
		[Export]
		private AnimationPlayer completionAnimator;
		private void StartCompletionDemo()
		{
			EmitSignal(SignalName.LevelDemoStarted);

			if (completionAnimator == null) return;

			OnCameraDemoAdvance();
			completionAnimator.Play("demo1");
		}

		/// <summary> Completion demo advanced, play a crossfade. </summary>
		public void OnCameraDemoAdvance()
		{
			StringName nextAnimation = completionAnimator.AnimationGetNext(completionAnimator.CurrentAnimation);
			if (completionAnimator.HasAnimation(nextAnimation))
				completionAnimator.Play(nextAnimation);
			CharacterController.instance.Camera.StartCrossfade();
		}

		#endregion


		/// <summary> Reference to active area's WorldEnvironment node. </summary>
		[Export]
		public WorldEnvironment Environment { get; private set; }
		public float StartingSaturation { get; private set; }
	}


	public struct SpawnData
	{
		/// <summary> Original parent node. </summary>
		public Node parentNode;
		/// <summary> Local transform to spawn with. </summary>
		public Transform3D spawnTransform;
		public SpawnData(Node parent, Transform3D transform)
		{
			parentNode = parent;
			spawnTransform = transform;
		}

		public void Respawn(Node n)
		{
			if (parentNode != null && n.GetParent() != parentNode)
			{
				if (n.IsInsideTree()) // Object needs to be reparented first.
					n.GetParent().RemoveChild(n);

				parentNode.AddChild(n);
			}

			n.SetDeferred("transform", spawnTransform);
		}
	}
}
