using Godot;

namespace Project.Gameplay.Triggers
{
	/// <summary>
	/// Parent class for all stage trigger modules.
	/// Connect a signal to Activate() or Deactivate(), or use a StageTrigger to automatically assign signals at runtime.
	/// </summary>
	public partial class StageTriggerModule : Node3D
	{
		protected CharacterController Character => CharacterController.instance;

		public virtual void Activate(Area3D _) => Activate();
		public virtual void Deactivate(Area3D _) => Deactivate();

		public virtual void Activate() { }
		public virtual void Deactivate() { }
		public virtual void Respawn() { }
	}
}
