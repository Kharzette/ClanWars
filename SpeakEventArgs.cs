using System;
using System.Collections.Generic;
using Eleon.Modding;


namespace ClanWarsModule
{
	internal class SpeakEventArgs : EventArgs
	{
		internal string	mPlayerSteamID;
		internal string	mMsg;
		internal bool	mbAlertMsg;
	}


	internal class ItemReturnEventArgs : EventArgs
	{
		internal string				mPlayerSteamID;
		internal List<ItemStack>	mItems;
	}
}
