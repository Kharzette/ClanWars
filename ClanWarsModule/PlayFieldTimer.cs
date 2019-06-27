using System;
using System.Timers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClanWarsModule
{
	internal class PlayFieldTimer : Timer
	{
		internal string	mPlayField;

		public PlayFieldTimer(Double interval, string pfName) : base(interval)
		{
			mPlayField	=pfName;
		}
	}
}
