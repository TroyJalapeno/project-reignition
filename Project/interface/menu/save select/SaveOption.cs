using Godot;
using Godot.Collections;
using Project.Core;

namespace Project.Interface.Menus;

public partial class SaveOption : Control
{
	[Export]
	private Sprite2D newData;
	[Export]
	private Control existingData;

	[Export]
	private Rect2 worldRingObtainedRegion;
	[Export]
	private Rect2 worldRingMissionRegion;
	[Export]
	private Sprite2D worldIcon;
	[Export]
	private Array<Rect2> worldIconRegions = [];
	[Export]
	private Label slotLabel;
	[Export]
	private Label slotShadowLabel;

	[Export]
	private Label levelLabel;
	[Export]
	private Label timeLabel;
	[Export]
	private Array<NodePath> worldRings = [];
	private Array<Sprite2D> _worldRings = [];

	public void SetUp()
	{
		for (int i = 0; i < worldRings.Count; i++)
			_worldRings.Add(GetNode<Sprite2D>(worldRings[i]));
	}

	private int saveIndex;
	public int SaveIndex
	{
		get => saveIndex;
		set
		{
			saveIndex = value;
			SaveManager.GameData saveData = SaveManager.GameSaveSlots[saveIndex];

			slotLabel.Text = slotShadowLabel.Text = (saveIndex + 1).ToString();

			if (saveData.IsNewFile()) //No data
			{
				newData.Visible = true;
				existingData.Visible = false;
				return;
			}

			newData.Visible = false;
			existingData.Visible = true;
			worldIcon.RegionRect = worldIconRegions[(int)saveData.lastPlayedWorld];

			levelLabel.Text = saveData.level.ToString("00");

			System.TimeSpan span = System.TimeSpan.FromSeconds(saveData.playTime);
			timeLabel.Text = span.ToString("hh\\:mm\\:ss");

			for (int i = 0; i < _worldRings.Count; i++)
			{
				//Check if world ring is unlocked (+1 because lost prologue doesn't have one.)
				bool isWorldRingObtained = saveData.IsWorldRingObtained((SaveManager.WorldEnum)i + 1);
				_worldRings[i].GetChild<Sprite2D>(0).Visible = isWorldRingObtained; //Update visibility
				_worldRings[i].RegionRect = isWorldRingObtained ? worldRingObtainedRegion : worldRingMissionRegion;
			}
		}
	}
}
