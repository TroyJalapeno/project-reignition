namespace Project.Core
{
	public static class CheatManager
	{
		private static bool EnableCheats => true;

		public static bool InfiniteSoulGauge => EnableCheats && false;
		public static bool SkipCountdown => EnableCheats && true;
	}
}
