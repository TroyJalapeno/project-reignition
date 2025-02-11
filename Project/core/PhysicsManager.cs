using Godot;
using Godot.Collections;

namespace Project.Core;

public partial class PhysicsManager : Node3D
{
	public static PhysicsManager Instance { get; private set; }

	public static float normalDelta;
	public static float physicsDelta;
	public static PhysicsDirectSpaceState3D physicsState;

	public override void _EnterTree() => Instance = this;

	private void UpdatePhysicsState() => physicsState = GetWorld3D().DirectSpaceState; //Update world's physics space

	public override void _Process(double delta) => normalDelta = (float)delta;

	public override void _PhysicsProcess(double delta)
	{
		physicsDelta = (float)delta;
		UpdatePhysicsState();
	}

	public const string PLAYER_GROUP = "player";
	public const string ENEMY_TAG = "enemy";

	public const float COLLISION_PADDING = 0.02f;

	public const uint ENVIRONMENT_COLLISION_MASK = 1; //Collision mask for environment
	private const string RAYCAST_POSITION = "position";
	private const string RAYCAST_NORMAL = "normal";
	private const string RAYCAST_COLLIDER = "collider";

	public static RaycastHit CastRay(Vector3 pos, Vector3 dir, bool hitAreas, uint mask = 2147483647, Array<Rid> ex = null)
	{
		Vector3 endPos = pos + dir;

		PhysicsRayQueryParameters3D rayQuery = new()
		{
			From = pos,
			To = endPos,
			CollisionMask = mask,
			CollideWithBodies = true,
			CollideWithAreas = hitAreas,
			HitBackFaces = false
		};

		if (ex != null)
			rayQuery.Exclude = ex;

		Dictionary result = physicsState.IntersectRay(rayQuery);
		RaycastHit raycast = new()
		{
			startPoint = pos,
			endPoint = endPos,
			direction = dir
		};

		if (result.Count != 0)
		{
			raycast.point = (Vector3)result[RAYCAST_POSITION];
			raycast.normal = (Vector3)result[RAYCAST_NORMAL];
			raycast.collidedObject = (Node)result[RAYCAST_COLLIDER];
			if (raycast)
				raycast.distance = pos.DistanceTo(raycast.point);
		}

		//Fix memory leaks
		if (ex != null)
		{
			ex.Clear();
			rayQuery.Exclude.Clear();
		}

		rayQuery.Dispose();
		result.Dispose();
		return raycast;
	}
}

public struct RaycastHit
{
	public static implicit operator bool(RaycastHit sp) => sp.collidedObject != null;

	public Vector3 startPoint;
	public Vector3 endPoint;
	public Vector3 direction;
	public Node collidedObject;
	public Vector3 normal;
	public Vector3 point;
	public float distance;
	public int zIndex;

	public bool Add(RaycastHit hit) //Attempts to add the values of 2 raycasts together. Returns TRUE if successful
	{
		if (!hit) //Invalid raycast
			return false;

		if (!this)
			collidedObject = hit.collidedObject;

		normal += hit.normal;
		point += hit.point;
		direction += hit.direction;
		distance += hit.distance;
		return true;
	}

	public bool Divide(float amount) //Attempts to divide the values of a raycast. Returns TRUE if successful
	{
		if (Mathf.IsZeroApprox(amount) || !this)
			return false;

		normal /= amount;
		point /= amount;
		direction /= amount;
		distance /= amount;
		return true;
	}
}