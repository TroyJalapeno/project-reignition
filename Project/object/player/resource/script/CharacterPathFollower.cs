using Godot;
using Project.Core;

namespace Project.Gameplay
{
	/// <summary>
	/// Helps keep track of where the player is relative to the level's path
	/// </summary>
	public partial class CharacterPathFollower : PathFollow3D
	{
		public CharacterController Character => CharacterController.instance;

		/// <summary> Current rotation of the pathfollower, in global radians. </summary>
		public float ForwardAngle { get; private set; }
		/// <summary> Current backwards rotation of the pathfollower, always equal to ForwardAngle + Mathf.Pi. </summary>
		public float BackAngle { get; private set; }
		/// <summary> Delta between last frame and current frame. Updates on Resync(). </summary>
		public float DeltaAngle { get; private set; }
		public Path3D ActivePath { get; private set; }
		/// <summary> Local delta to player position. </summary>
		public Vector3 FlatPlayerPositionDelta { get; private set; }
		/// <summary> "True" local delta to player position using Basis.Inverse(). </summary>
		public Vector3 TruePlayerPositionDelta { get; private set; }

		/// <summary> Custom up axis. Equal to Forward() rotated 90 degrees around RightAxis. </summary>
		public Vector3 UpAxis { get; private set; }
		/// <summary> Custom right axis. Cross product of Forward() and Vector3.Up [Fallback: ForwardAxis] </summary>
		public Vector3 RightAxis { get; private set; }
		/// <summary> Custom forward axis. Equal to Vector3.Forward.Rotated(Vector3.Up, ForwardAngle) </summary>
		public Vector3 ForwardAxis { get; private set; }

		public void SetActivePath(Path3D newPath)
		{
			if (newPath == null || newPath == ActivePath) return;

			if (IsInsideTree()) //Unparent
				GetParent().RemoveChild(this);
			newPath.AddChild(this);

			ActivePath = newPath;
			CallDeferred(MethodName.Resync);
		}

		public void Resync()
		{
			if (!IsInsideTree()) return;
			if (ActivePath == null) return;

			Vector3 syncPoint = Character.GlobalPosition;
			/*
			if (!Character.IsOnGround)
			{
				RaycastHit groundCheck = this.CastRay(Character.GlobalPosition, -Character.UpDirection * 10f, RuntimeConstants.Instance.environmentMask);
				if (groundCheck && groundCheck.collidedObject.IsInGroup("floor"))
					syncPoint.y = groundCheck.point.y;
			}
			*/
			Progress = ActivePath.Curve.GetClosestOffset(syncPoint - ActivePath.GlobalPosition);

			float newForwardAngle = CharacterController.CalculateForwardAngle(this.Forward());
			DeltaAngle = ExtensionMethods.SignedDeltaAngleRad(newForwardAngle, ForwardAngle) * .5f;
			ForwardAngle = newForwardAngle;

			BackAngle = ForwardAngle + Mathf.Pi;
			FlatPlayerPositionDelta = (Character.GlobalPosition - GlobalPosition).Rotated(Vector3.Up, -ForwardAngle);
			TruePlayerPositionDelta = Basis.Inverse() * (Character.GlobalPosition - GlobalPosition);

			//Update custom orientations
			ForwardAxis = Vector3.Forward.Rotated(Vector3.Up, ForwardAngle).Normalized();
			float upDotProduct = this.Forward().Dot(Vector3.Up);
			if (upDotProduct < .9f)
				RightAxis = this.Forward().Cross(Vector3.Up).Normalized();
			else //Moving straight up/down
				RightAxis = this.Forward().Cross(ForwardAxis).Normalized();
			UpAxis = this.Forward().Rotated(RightAxis, Mathf.Pi * .5f).Normalized();

			Debug.DrawRay(GlobalPosition, ForwardAxis, Colors.Blue);
			Debug.DrawRay(GlobalPosition, RightAxis, Colors.Red);
			Debug.DrawRay(GlobalPosition, UpAxis, Colors.Green);
		}

		/*
		/// <summary>
		/// GetClosestOffset() seems to be broken in 4.0, so here's a more accurate (allbeit slower) method.
		/// Loops over all baked points in a path, so try to restrict it's usage as much as possible.
		/// UPDATE v4.0.beta8.mono.official [45cac42c0] seems to have fixed this. Leaving this as a fallback.
		/// </summary>
		private void UpdatePosition()
		{
			Vector3 targetPosition = Character.GlobalPosition - ActivePath.GlobalPosition;
			Vector3[] points = ActivePath.Curve.GetBakedPoints();
			float closestPointDistance = Mathf.Inf;
			int closestPointIndex = -1;

			//Get the closest baked point
			for (int i = 0; i < points.Length; i++)
			{
				float distance = points[i].DistanceTo(targetPosition);
				if (distance < closestPointDistance)
				{
					closestPointIndex = i;
					closestPointDistance = distance;
				}
			}

			Progress = ActivePath.Curve.GetClosestOffset(targetPosition); //Estimate for external objects to reference

			if (closestPointIndex >= points.Length - 1) //Limit point index
				closestPointIndex--;

			//Assign transform
			Vector3 position = points[closestPointIndex];
			Vector3 nextPoint = points[closestPointIndex + 1];
			Vector3 forwardDirection = (position - nextPoint).Normalized();

			//Attempt to interpolate between baked points (Spaghetti code that seems to work alright)
			if (closestPointIndex != 0 && closestPointIndex != points.Length)
			{
				float nextDistance = nextPoint.DistanceTo(position);
				targetPosition = (targetPosition - position).Rotated(Vector3.Up, -forwardDirection.SignedAngleTo(Vector3.Forward, Vector3.Up));

				float t = 1.0f - Mathf.Clamp(targetPosition.x / nextDistance, 0f, 1f);
				position = position.Lerp(nextPoint, t);
			}

			position += ActivePath.GlobalPosition;
			LookAtFromPosition(position, position + forwardDirection, this.Up());
		}
		*/

		//Is the pathfollower ahead of the reference point?
		public bool IsAheadOfPoint(Vector3 globalPosition) => Mathf.Sign(Progress - ActivePath.Curve.GetClosestOffset(globalPosition - ActivePath.GlobalPosition)) > 0;
	}
}
