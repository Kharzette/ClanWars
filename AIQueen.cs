using System.Reflection;
using System.Collections.Generic;
using System.IO;
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
		Dictionary<int, int>	mIndividualScores	=new Dictionary<int, int>();

		PVector3	mTyadaPos, mCastaePos;

		float		mNearDistance;	//how far away to contribute ingotses?


		internal AIQueen(float nearDist, ModGameAPI mgapi)
		{
			mNearDistance	=nearDist;

			LoadPointValues(mgapi);
		}


		internal void SetTyadaTemplePos(PVector3 tyPos)
		{
			mTyadaPos	=tyPos;
		}


		internal void SetCastaeTemplePos(PVector3 casPos)
		{
			mCastaePos	=casPos;
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


		internal void SacrificeItems(int entID, ItemStack []items)
		{
			int	totalValue	=0;

			foreach(ItemStack st in items)
			{
				if(mWantedItemsValue.ContainsKey(st.id))
				{
					int	val	=mWantedItemsValue[st.id].mValue * st.count;

					totalValue	+=val;

					mIndividualScores[entID]	+=val;
				}
			}
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
	}
}
