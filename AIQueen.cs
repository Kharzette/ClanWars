using System;
using System.IO;
using System.Timers;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using Eleon.Modding;


namespace ClanWarsModule
{
	internal class AIQueen
	{
		internal class Goody
		{
			internal int	mID;
			internal string	mName;
			internal int	mValue;
		}

		Dictionary<int, Goody>	mWantedItemsValue	=new Dictionary<int, Goody>();
		Dictionary<string, int>	mIndividualScores	=new Dictionary<string, int>();

		//clan membership
		List<string>	mTyadaMembers	=new List<string>();
		List<string>	mCastaeMembers	=new List<string>();

		//my position on the starter worlds
		PVector3	mTyadaPos, mCastaePos;

		//how far away to contribute goodies?
		float		mNearDistance;

		//timers
		Timer	mQuarter;
		Timer	mFinalMinute;
		Timer	mFinalSeconds;
		Timer	mGameOver;
		int		mQuartersElapsed;

		internal event EventHandler	eSpeakToPlayer;
		internal event EventHandler	eSpeakToPlayerClan;
		internal event EventHandler	eSpeakToAll;
		internal event EventHandler	eReturnItems;
		internal event EventHandler	eGameEnd;


		internal AIQueen(float nearDist, ModGameAPI mgapi)
		{
			mNearDistance	=nearDist;

			LoadPointValues(mgapi);
		}


		internal void StartMatch(int matchDuration)
		{
			Debug.Assert(matchDuration > 1);

			long	matchDurSec		=matchDuration * 60;
			long	quarterTimeSec	=(matchDurSec) / 4;
			long	finalMin		=matchDurSec - 60;
			long	lastTen			=matchDurSec - 10;

			mQuarter		=new Timer(quarterTimeSec * 1000);
			mFinalMinute	=new Timer(finalMin * 1000);
			mFinalSeconds	=new Timer(lastTen * 1000);
			mGameOver		=new Timer(matchDurSec * 1000);

			mQuarter.AutoReset	=true;

			mQuarter.Start();
			mFinalMinute.Start();
			mFinalSeconds.Start();
			mGameOver.Start();

			//wire elapsed events
			mQuarter.Elapsed		+=OnQuarter;
			mFinalMinute.Elapsed	+=OnFinalMinute;
			mFinalSeconds.Elapsed	+=OnFinalSeconds;
			mGameOver.Elapsed		+=OnGameOver;
		}


		internal void SetTyadaTemplePos(PVector3 tyPos)
		{
			mTyadaPos	=tyPos;
		}


		internal void SetCastaeTemplePos(PVector3 casPos)
		{
			mCastaePos	=casPos;
		}


		internal void DeductResCost(string steamID, int cost)
		{
		}


		internal bool bCheckNear(PVector3 playerPos, bool bTyada)
		{
			if(bTyada)
			{
				float	dist	=VecStuff.Distance(playerPos, mTyadaPos);
				if(dist < mNearDistance)
				{
					return	true;
				}
			}
			else
			{
				float	dist	=VecStuff.Distance(playerPos, mCastaePos);
				if(dist < mNearDistance)
				{
					return	true;
				}
			}
			return	false;
		}


		internal void SacrificeItems(string steamID, ItemStack []items)
		{
			int	totalValue	=0;

			if(!mIndividualScores.ContainsKey(steamID))
			{
				mIndividualScores.Add(steamID, 0);
			}

			bool	bSomeUseless	=false;
			bool	bSomeUseful		=false;

			List<ItemStack>	mUseless	=new List<ItemStack>();

			foreach(ItemStack st in items)
			{
				if(mWantedItemsValue.ContainsKey(st.id))
				{
					int	val	=mWantedItemsValue[st.id].mValue * st.count;

					totalValue	+=val;

					mIndividualScores[steamID]	+=val;

					bSomeUseful	=true;
				}
				else
				{					
					bSomeUseless	=true;
					mUseless.Add(st);
				}
			}

			if(bSomeUseful && bSomeUseless)
			{
				SpeakToPlayer(steamID, "Some of that was useful, thank you!  I grant your clan " + totalValue + " points!");
			}
			else if(bSomeUseful)
			{
				SpeakToPlayer(steamID, "Excellent, thank you!  I grant your clan " + totalValue + " points!");
			}
			else if(bSomeUseless)
			{
				SpeakToPlayer(steamID, "I have no need for these items.");
			}
			else
			{
				SpeakToPlayer(steamID, "Very well, return later when you have a proper sacrifice.");
			}

			ReturnItems(steamID, mUseless);

			//msg to clan?
			//mgapi.Console_Write("Styx awarding " + totalValue + " points to player " + steamID);
		}


		long	CalcScore(bool bTyada)
		{
			long	ret	=0;

			foreach(KeyValuePair<string, int> sc in mIndividualScores)
			{
				if(bTyada)
				{
					if(mTyadaMembers.Contains(sc.Key))
					{
						ret	+=sc.Value;
					}
				}
				else
				{
					if(mCastaeMembers.Contains(sc.Key))
					{
						ret	+=sc.Value;
					}
				}
			}
			return	ret;
		}


		void ReturnItems(string steamID, List<ItemStack> items)
		{
			ItemReturnEventArgs	irea	=new ItemReturnEventArgs();

			irea.mItems			=items;
			irea.mPlayerSteamID	=steamID;

			eReturnItems?.Invoke(null, irea);
		}


		void SpeakToPlayer(string steamID, string msg)
		{
			SpeakEventArgs	sea	=new SpeakEventArgs();

			sea.mPlayerSteamID	=steamID;
			sea.mMsg			=msg;

			eSpeakToPlayer?.Invoke(this, sea);
		}


		void SpeakToPlayerClan(string steamID, string msg)
		{
			SpeakEventArgs	sea	=new SpeakEventArgs();

			sea.mPlayerSteamID	=steamID;
			sea.mMsg			=msg;

			eSpeakToPlayerClan?.Invoke(this, sea);
		}


		void SpeakToAll(string msg, bool bAlert)
		{
			SpeakEventArgs	sea	=new SpeakEventArgs();

			sea.mMsg		=msg;
			sea.mbAlertMsg	=bAlert;

			eSpeakToAll?.Invoke(null, sea);
		}


		void LoadPointValues(ModGameAPI mgapi)
		{
			//can't be too sure of what the current directory is
			Assembly	ass	=Assembly.GetExecutingAssembly();

			string	dllDir	=Path.GetDirectoryName(ass.Location);

			string	filePath	=Path.Combine(dllDir, "ItemPointValues.txt");

			mgapi.Console_Write("Loading Config file for the queen's point values: " + filePath);

			FileStream	fs	=new FileStream(filePath, FileMode.Open, FileAccess.Read);
			if(fs == null)
			{
				return;
			}

			StreamReader	sr	=new StreamReader(fs);
			if(sr == null)
			{
				return;
			}

			while(!sr.EndOfStream)
			{
				string	line	=sr.ReadLine();

				string	[]toks	=line.Split(' ', '\t');

//				mgapi.Console_Write("Got " + toks.Length + " tokenses for line " + line);

				if(toks.Length < 3)
				{
					//bad line
					mgapi.Console_Write("Bad line in point config file at position: " + sr.BaseStream.Position);
					continue;
				}

				//skip whitespace
				int	idx	=0;
				while(idx < toks.Length)
				{
					if(toks[idx] == "" || toks[idx] == " " || toks[idx] == "\t")
					{
						idx++;
						continue;
					}
					break;
				}

				if(toks[idx].StartsWith("//"))
				{
					continue;
				}

				Goody	good	=new Goody();

				if(!int.TryParse(toks[idx], out good.mID))
				{
					mgapi.Console_Write("Bad token looking for item id in point config file at position: " + sr.BaseStream.Position);
					continue;
				}

				idx++;

				while(idx < toks.Length)
				{
					if(toks[idx] == "" || toks[idx] == " " || toks[idx] == "\t")
					{
						idx++;
						continue;
					}
					break;
				}

				//this one should be in quotes
				if(toks[idx][0] != '\"')
				{
					mgapi.Console_Write("Expecting \" looking for item name, got " + toks[idx] + " in point config file at position: " + sr.BaseStream.Position);
					continue;
				}

				//one word?
				if(toks[idx].EndsWith("\""))
				{
					good.mName	=toks[idx].Substring(1, toks[idx].Length - 2);
					idx++;
				}
				else
				{
					//tokens ahead will have the end quote
					good.mName	=toks[idx].Substring(1, toks[idx].Length - 1);
					good.mName	+=" ";	//spaces are chopped out

					idx++;
					//tack on tokens till the trailing " is hit
					while(idx < toks.Length)
					{
						if(toks[idx].EndsWith("\""))
						{
							//found the trailing quote
							good.mName	+=toks[idx].Substring(0, toks[idx].Length - 1);
							idx++;
							break;
						}

						good.mName	+=toks[idx];
						idx++;
					}
				}

				while(idx < toks.Length)
				{
					if(toks[idx] == "" || toks[idx] == " " || toks[idx] == "\t")
					{
						idx++;
						continue;
					}
					break;
				}

				if(!int.TryParse(toks[idx], out good.mValue))
				{
					mgapi.Console_Write("Bad token looking for value in point config file at position: " + sr.BaseStream.Position);
					continue;
				}

				mWantedItemsValue.Add(good.mID, good);
			}

			sr.Close();
			fs.Close();

			foreach(KeyValuePair<int, Goody> goods in mWantedItemsValue)
			{
				mgapi.Console_Write("Goody: " + goods.Value.mID + ", " + goods.Value.mName + ", " + goods.Value.mValue);
			}
		}


		void OnGameOver(Object sender, ElapsedEventArgs e)
		{
			long	tyadaScore	=CalcScore(true);
			long	castScore	=CalcScore(false);

			if(tyadaScore == castScore)
			{
				SpeakToAll("The game ends with a tie!  What a wonderful match.  My people were very entertained!", true);
			}
			else if(tyadaScore > castScore)
			{
				SpeakToAll("Tyada is victorious!  Their glory shall live on for eternity in our memory cells!", true);
			}
			else
			{
				SpeakToAll("Castae is victorious!  Their glory shall live on for eternity in our memory cells!", true);
			}
			eGameEnd?.Invoke(null, null);
		}


		void OnFinalSeconds(Object sender, ElapsedEventArgs e)
		{
			SpeakToAll("Ten seconds!", false);
		}


		void OnFinalMinute(Object sender, ElapsedEventArgs e)
		{
			long	tyadaScore	=CalcScore(true);
			long	castScore	=CalcScore(false);

			string	update	="";


			if(tyadaScore == castScore)
			{
				update	+="The score is tied ";
			}
			else if(tyadaScore > castScore)
			{
				update	+="Tyada is in the lead ";
			}
			else
			{
				update	+="Castae is in the lead ";
			}

			update	+="with ONE MINUTE remaining!";

			SpeakToAll(update, true);
		}


		void OnQuarter(Object sender, ElapsedEventArgs e)
		{
			mQuartersElapsed++;

			if(mQuartersElapsed > 3)
			{
				//game over!
				return;
			}

			long	QuarterIntervalMS	=(long)mQuarter.Interval;
			long	QuarterIntervalSec	=QuarterIntervalMS / 1000;

			long	timeElapsedSec		=QuarterIntervalSec * mQuartersElapsed;
			long	timeRemainingSec	=(QuarterIntervalSec * 4) - timeElapsedSec;
			long	timeRemainingMin	=timeRemainingSec / 60;
			long	timeRemainderSec	=timeRemainingSec % 60;

			long	tyadaScore	=CalcScore(true);
			long	castScore	=CalcScore(false);

			string	update	="";


			if(tyadaScore == castScore)
			{
				update	+="The score is tied ";
			}
			else if(tyadaScore > castScore)
			{
				update	+="Tyada is in the lead ";
			}
			else
			{
				update	+="Castae is in the lead ";
			}

			if(mQuartersElapsed != 2)
			{
				update	+="with " + timeRemainingMin + " minutes and " + timeRemainderSec + " seconds remaining.";
			}
			else if(mQuartersElapsed == 2)
			{
				update	+=" at half time remaining.";
			}

			SpeakToAll(update, false);
		}
	}
}
