using Godot;
using Godot.Collections;
using Project.Core;

namespace Project.Gameplay.Objects;

/// <summary>
/// Launches the player. Use <see cref="CreateLaunchSettings(Vector3, Vector3, float, bool)"/> to bypass needing a Launcher node.
/// </summary>
[Tool]
public partial class Launcher : Area3D // Jumps between static points w/ custom sfx support
{
	#region Editor
	private const string SecondarySettingsEnabled = "Secondary Settings/Enabled";
	private const string SecondarySettingsMiddleHeight = "Secondary Settings/Middle Height";
	private const string SecondarySettingsFinalHeight = "Secondary Settings/Final Height";
	private const string SecondarySettingsDistance = "Secondary Settings/Distance";
	private const string SecondarySettingsBlend = "Secondary Settings/Blend";
	public override Array<Dictionary> _GetPropertyList()
	{
		Array<Dictionary> properties =
			[
				ExtensionMethods.CreateCategory("Secondary Settings"),
				ExtensionMethods.CreateProperty(SecondarySettingsEnabled, Variant.Type.Bool)
			];

		if (!enableSecondarySettings)
			return properties;

		properties.Add(ExtensionMethods.CreateProperty(SecondarySettingsMiddleHeight, Variant.Type.Float));
		properties.Add(ExtensionMethods.CreateProperty(SecondarySettingsFinalHeight, Variant.Type.Float));
		properties.Add(ExtensionMethods.CreateProperty(SecondarySettingsDistance, Variant.Type.Float));
		properties.Add(ExtensionMethods.CreateProperty(SecondarySettingsBlend, Variant.Type.Float, PropertyHint.Range, "0, 1, .1"));

		return properties;
	}

	public override Variant _Get(StringName property)
	{
		switch ((string)property)
		{
			case SecondarySettingsEnabled:
				return enableSecondarySettings;
			case SecondarySettingsMiddleHeight:
				return secondaryMiddleHeight;
			case SecondarySettingsFinalHeight:
				return secondaryFinalHeight;
			case SecondarySettingsDistance:
				return secondaryDistance;
			case SecondarySettingsBlend:
				return launchRatio;
		}

		return base._Get(property);
	}

	public override bool _Set(StringName property, Variant value)
	{
		switch ((string)property)
		{
			case SecondarySettingsEnabled:
				enableSecondarySettings = (bool)value;
				NotifyPropertyListChanged();
				break;
			case SecondarySettingsMiddleHeight:
				secondaryMiddleHeight = (float)value;
				break;
			case SecondarySettingsFinalHeight:
				secondaryFinalHeight = (float)value;
				break;
			case SecondarySettingsDistance:
				secondaryDistance = (float)value;
				break;
			case SecondarySettingsBlend:
				launchRatio = (float)value;
				break;
			default:
				return false;
		}

		return true;
	}
	#endregion

	[Signal]
	public delegate void ActivatedEventHandler();
	[Signal]
	public delegate void DeactivatedEventHandler(); // Called after character finishes processing this launcher.

	/// <summary> Height at the beginning of the arc. </summary>
	[ExportCategory("Launch Settings")]
	[Export]
	private float startingHeight;
	/// <summary> Height at the highest point of the arc. </summary>
	[Export]
	private float middleHeight;
	/// <summary> Height at the end of the arc. </summary>
	[Export]
	private float finalHeight;
	/// <summary> How far to travel. </summary>
	[Export]
	private float distance;

	/// <summary> Should the player be allowed to perform a Jump Dash after the launch is completed? </summary>
	[Export]
	public bool allowJumpDashing;

	[Export]
	public LaunchDirection launchDirection;
	public enum LaunchDirection
	{
		Forward,
		Up,
		Flatten,
	}

	/// <summary> Allows the launcher to blend between secondary launch settings. </summary>
	protected bool enableSecondarySettings;
	/// <summary> Controls the blend between launch settings. Mostly used for editor previewing. </summary>
	protected float launchRatio;
	protected float secondaryMiddleHeight;
	protected float secondaryFinalHeight;
	protected float secondaryDistance;

	/// <summary> Returns the blend value between the launch settings. Can be overridden by scripts that inherit Launcher.cs. </summary>
	public virtual float GetLaunchRatio()
	{
		if (enableSecondarySettings)
			return launchRatio;

		return 0; // Use the primary launch settings
	}

	public virtual Vector3 GetLaunchDirection()
	{
		if (launchDirection == LaunchDirection.Forward)
			return this.Forward();
		else if (launchDirection == LaunchDirection.Flatten)
			return this.Up().RemoveVertical().Normalized();

		return this.Up();
	}

	public Vector3 StartingPoint => GlobalPosition + (Vector3.Up * startingHeight);

	public LaunchSettings GetLaunchSettings()
	{
		float blendedDistance = Mathf.Lerp(distance, secondaryDistance, GetLaunchRatio());
		float blendedMiddleHeight = Mathf.Lerp(middleHeight, secondaryMiddleHeight, GetLaunchRatio());
		float blendedFinalHeight = Mathf.Lerp(finalHeight, secondaryFinalHeight, GetLaunchRatio());

		Vector3 startPosition = GlobalPosition + (Vector3.Up * startingHeight);
		Vector3 endPosition = startPosition + (GetLaunchDirection() * blendedDistance) + (Vector3.Up * blendedFinalHeight);

		LaunchSettings settings = LaunchSettings.Create(startPosition, endPosition, blendedMiddleHeight);
		settings.UseAutoAlign = true;

		return settings;
	}

	public virtual void Activate(Area3D a)
	{
		EmitSignal(SignalName.Activated);

		sfxPlayer?.Play();

		if (voiceKey?.IsEmpty == false)
			Character.Effect.PlayVoice(voiceKey);

		IsCharacterCentered = recenterSpeed == 0;
		LaunchSettings LaunchSettings = GetLaunchSettings();
		Character.StartLauncher(LaunchSettings, this);

		if (LaunchSettings.InitialVelocity.AngleTo(Vector3.Up) < Mathf.Pi * .1f)
			Character.Animator.JumpAnimation();
		else
			Character.Animator.LaunchAnimation();
	}

	[Export]
	private int recenterSpeed; // How fast to recenter the character

	public virtual bool IsCharacterCentered { get; private set; }
	protected CharacterController Character => CharacterController.instance;

	public Vector3 RecenterCharacter()
	{
		Vector3 pos = Character.GlobalPosition.MoveToward(StartingPoint, recenterSpeed * PhysicsManager.physicsDelta);
		IsCharacterCentered = pos.IsEqualApprox(StartingPoint);
		return pos;
	}

	public void Deactivate() => EmitSignal(SignalName.Deactivated);

	[Export]
	/// <summary> Optional SFX player. </summary>
	private AudioStreamPlayer3D sfxPlayer;
	[Export]
	/// <summary> Option voice to play. </summary>
	private StringName voiceKey;
}