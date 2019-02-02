using System;
using System.Reflection;
using System.Timers;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Eleon.Modding;
using System.Diagnostics;

namespace ClanWarsModule
{
    public class ClanWars : ModInterface
    {
		internal class Deadites
		{
			internal int		mEntID;
			internal PVector3	mPos;	//position at this stage
		}

        ModGameAPI	mGameAPI;

		Random	mRand;

		AIQueen	mStyx;

		//starter playfield auto start stuff
		List<Process>	mPlayFieldServers;
		bool			mbRequestedTyadaLoad, mbRequestedCastaeLoad;
		Timer			mStarterLoadTimer;

		//constants from a config file
		Dictionary<string, int>			mIConstants;
		Dictionary<string, PVector3>	mVConstants;

		//tracking playfields in use
		Dictionary<string, PlayFieldTimer>	mPlayFieldDeSpawnTimers;

		//start pad position stuff
		bool			mbStartsRecordedTyada, mbStartsRecordedCastae;
		List<PVector3>	mStartPositionsTyada, mStartPositionsCastae;

		//structure ids for the rally buildings
		int	mTyadaRallyID, mCastaeRallyID;

		//structure ids for the temple buildings
		int	mTyadaTempleID, mCastaeTempleID;

		//playfield ids for the homeworlds
		int	mTyadaPFID, mCastaePFID;

		//players in the match
        List<PlayerInfo>	mClanCastae;
        List<PlayerInfo>	mClanTyada;

		//steam id to client id conversions
		Dictionary<int, string>	mPlayerCIDToSteamID			=new Dictionary<int, string>();	
		Dictionary<string, int>	mPlayerSteamIDToClientID	=new Dictionary<string, int>();
		Dictionary<int, int>	mPlayerEntIDToClientID		=new Dictionary<int, int>();
		Dictionary<string, int>	mPlayerSteamIDToEntID		=new Dictionary<string, int>();

		//positions and rotations updated at intervals
		Dictionary<int, IdPositionRotation>	mPlayerPosNRots;

		//current playfield of players
		Dictionary<int, string>	mPlayerCurPlayFields;

		//players disconnected during a match (they can rejoin)
        List<string>	mCastaeDiscos;
        List<string>	mTyadaDiscos;

		//players currently dead and respawning
		List<Deadites>	mDeadPlayers;

		//players that are talking to the queen
		List<int>	mPlayersSacrificing;

		//timestamp when a player gave items to the queen
		Dictionary<int, DateTime>	mPlayerSacTimes;

		//is the match in progress?
		bool	mbMatchStarted	=false;

		//clans created?
		bool	mbClanTyadaCreated, mbClanCastaeCreated;

		//timers watching for respawn / movement
		Dictionary<Timer, int>	mActiveTimers;

		//timer to poll for vital game entity data
		Timer	mGameDataTimer;

		//timer to update positions for all players
		Timer	mPosRotTimer;

		//countdown timer
		Timer	mCountDownTimer;
		int		mSecondRemaining;

		//this whole section should come from a config file
		const float		SpawnDetectDistanceMin	=1;		//movement indicating player is in control
		const float		SpawnDetectDistanceMax	=20;	//movement indicating player is in control
		const double	MoveTestInterval		=500;	//wild guess
		const double	GameDataPollInterval	=2000;	//periodic retry to get valid data for start spots and doors and such
		const float		NearQueenDistance		=10f;


        public void Game_Start(ModGameAPI dediAPI)
        {
            mGameAPI	=dediAPI;

			mRand	=new Random();
			mStyx	=new AIQueen(NearQueenDistance);

			//wire events
			mStyx.eReturnItems			+=OnStyxReturnItems;
			mStyx.eSpeakToAll			+=OnStyxSpeakToAll;
			mStyx.eSpeakToPlayer		+=OnStyxSpeakToPlayer;
			mStyx.eSpeakToPlayerClan	+=OnStyxSpeakToPlayerClan;
			mStyx.eGameEnd				+=OnStyxGameEnd;
			mStyx.eDebugSpew			+=OnDebugSpew;

			mIConstants	=new Dictionary<string, int>();
			mVConstants	=new Dictionary<string, PVector3>();

			mClanCastae	=new List<PlayerInfo>();
			mClanTyada	=new List<PlayerInfo>();

			mCastaeDiscos	=new List<string>();
			mTyadaDiscos	=new List<string>();

			mDeadPlayers	=new List<Deadites>();

			mPlayersSacrificing	=new List<int>();
			mPlayerSacTimes	=new Dictionary<int, DateTime>();

			mStartPositionsTyada		=new List<PVector3>();
			mStartPositionsCastae		=new List<PVector3>();
			mbStartsRecordedCastae		=false;
			mbStartsRecordedTyada		=false;

			mActiveTimers	=new Dictionary<Timer, int>();

			mGameDataTimer	=new Timer(GameDataPollInterval);

			mGameDataTimer.Elapsed		+=OnGameDataTimer;
			mGameDataTimer.AutoReset	=true;
			mGameDataTimer.Start();

			mPlayerPosNRots			=new Dictionary<int, IdPositionRotation>();
			mPlayerCurPlayFields	=new Dictionary<int, string>();
			mPlayFieldDeSpawnTimers	=new Dictionary<string, PlayFieldTimer>();
			mPlayFieldServers		=new List<Process>();

			LoadConstants();

            mGameAPI.Console_Write("Clan vs Clan action!");
        }


        public void Game_Event(CmdId eventId, ushort seqNr, object data)
        {
            try
            {
                switch (eventId)
                {
					case CmdId.Event_Player_ItemExchange:
						HandleSacrificeItems(data as ItemExchangeInfo);
						break;

					case CmdId.Event_Playfield_List:
						PlayfieldList	pfl	=data as PlayfieldList;
						foreach(string pf in pfl.playfields)
						{
							mGameAPI.Console_Write("Playfield: " + pf);
							if(pf == "Tyada" && !mbStartsRecordedTyada)
							{
								mGameAPI.Game_Request(CmdId.Request_GlobalStructure_Update, (ushort)0, new PString("Tyada"));
							}
							else if(pf == "Castae" && !mbStartsRecordedCastae)
							{
								mGameAPI.Game_Request(CmdId.Request_GlobalStructure_Update, (ushort)0, new PString("Castae"));
							}
						}
						break;

					case CmdId.Event_GlobalStructure_List:
						HandleGlobalStructureList(data as GlobalStructureList);
						break;

					//this event doesn't necessarily indicate a join,
					//but is mostly join related
                    case CmdId.Event_Player_Info:
						HandlePlayerInfo(data as PlayerInfo);
                        break;

					case CmdId.Event_Entity_PosAndRot:
						IdPositionRotation	idpr	=data as IdPositionRotation;
						if(idpr == null)
						{
							return;
						}

						//update player positions if this is called for a player
						if(mPlayerPosNRots.ContainsKey(idpr.id))
						{
							mPlayerPosNRots[idpr.id].pos	=idpr.pos;
							mPlayerPosNRots[idpr.id].rot	=idpr.rot;

//							mGameAPI.Console_Write("PosNRot update for player: " + idpr.id + " : " + VectorToString(idpr.pos));
							CheckStyx(idpr.id);
						}
						break;

                    case CmdId.Event_Player_Connected:
						mGameAPI.Console_Write("spam: Player connected: " + ((Id)data).id);
                        mGameAPI.Game_Request(CmdId.Request_Player_Info, 0, (Id)data);
                        break;

                    case CmdId.Event_Player_Disconnected:
			            mGameAPI.Console_Write("spam: Event_Player_Disconnected");
						{
							Id	pid	=data as Id;
							if(pid == null)
							{
								break;
							}

							HandleDisconnect(pid.id);
						}
                        break;

                    case CmdId.Event_Statistics:
			            mGameAPI.Console_Write("spam: Event_Statistics");
                        StatisticsParam	stats	=(StatisticsParam)data;

                        if(stats.type == StatisticsType.PlayerDied)
                        {
							PlayerInfo	casResult	=mClanCastae.FirstOrDefault(e => e.entityId == stats.int1);
							PlayerInfo	tyResult	=mClanTyada.FirstOrDefault(e => e.entityId == stats.int1);

							if(casResult != null)
							{
								AddDead(casResult);
								TrackDeadPlayers(casResult);
							}
							else if(tyResult != null)
							{
								AddDead(tyResult);
								TrackDeadPlayers(tyResult);
							}
						}
                        break;

						//TODO: enemy chat scramble
                    case CmdId.Event_ChatMessage:
                        ChatInfo ci = (ChatInfo)data;
                        if (ci == null) { break; }

                        if (ci.type != 8 && ci.type != 7 && ci.msg == "!MODS")
                            ChatMessage("Clan Wars...");
                        break;

					case CmdId.Event_Player_ChangedPlayfield:
						IdPlayfield	idpf	=(IdPlayfield)data;
						if(idpf == null)
						{
							break;
						}

						mGameAPI.Console_Write("Id of " + idpf.id + " changed playfield to " + idpf.playfield + "...");

						if(mPlayerCurPlayFields.ContainsKey(idpf.id))
						{
							//see if anyone is still in the previous playfield
							string	newPF	=idpf.playfield;
							string	prev	=mPlayerCurPlayFields[idpf.id];
							int		num		=mPlayerCurPlayFields.Where(pf => pf.Value == prev).Count();
							if(num <= 1 && prev != "Tyada" && prev != "Castae")
							{
								mGameAPI.Console_Write("Scheduling close of empty playfield " + prev);
								CloseEmptyPlayField(prev);
							}

							//see if this playfield we are switching to was scheduled to be shut off
							if(mPlayFieldDeSpawnTimers.ContainsKey(newPF))
							{
								mGameAPI.Console_Write("Player entering a playfield about to be shut off, cancelling the shutoff...");
								mPlayFieldDeSpawnTimers[idpf.playfield].Stop();
								mPlayFieldDeSpawnTimers.Remove(newPF);
							}
							mPlayerCurPlayFields[idpf.id]	=newPF;
						}
						else
						{
							mGameAPI.Console_Write("Playfield change for player not in the playfield dict");
						}
						break;

					case CmdId.Event_Playfield_Entity_List:
						PlayfieldEntityList	pfel	=(PlayfieldEntityList)data;
						if(pfel == null)
						{
							break;
						}

						mGameAPI.Console_Write("Entity list for playfield " + pfel.playfield);

						foreach(EntityInfo ei in pfel.entities)
						{
							mGameAPI.Console_Write("ID: " + ei.id + ", Pos: " + ei.pos + ", Type: " + ei.type);
						}
						break;

                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                mGameAPI.Console_Write(ex.Message);
            }
        }


		public void Game_Update()
        {
        }


        public void Game_Exit()
        {
            mGameAPI.Console_Write("ClanWars: Exit");

			mStyx.eDebugSpew			-=OnDebugSpew;
			mStyx.eGameEnd				-=OnStyxGameEnd;
			mStyx.eReturnItems			-=OnStyxReturnItems;
			mStyx.eSpeakToAll			-=OnStyxSpeakToAll;
			mStyx.eSpeakToPlayer		-=OnStyxSpeakToPlayer;
			mStyx.eSpeakToPlayerClan	-=OnStyxSpeakToPlayerClan;
        }


		void TrackIDs(int clientID, int entID, string steamID)
		{
			//make sure not already in dictionary
			UnTrackIDs(clientID, entID, steamID);

			mPlayerCIDToSteamID.Add(clientID, steamID);
			mPlayerSteamIDToClientID.Add(steamID, clientID);
			mPlayerEntIDToClientID.Add(entID, clientID);
			mPlayerSteamIDToEntID.Add(steamID, entID);
		}


		void UnTrackIDs(int clientID, int entID, string steamID)
		{
			mPlayerCIDToSteamID.Remove(clientID);
			mPlayerSteamIDToClientID.Remove(steamID);
			mPlayerEntIDToClientID.Remove(entID);
			mPlayerSteamIDToEntID.Remove(steamID);
		}


		bool bHasPFProcess(Process p)
		{
			foreach(Process pc in mPlayFieldServers)
			{
				if(pc.Id == p.Id)
				{
					return	true;
				}
			}
			return	false;
		}


		void JoinTyada(PlayerInfo pi)
		{
			mGameAPI.Console_Write("Player " + pi.playerName + " joins Tyada...");
			AttentionMessage(pi.playerName + " has joined Clan Tyada!");
			mClanTyada.Add(pi);
			TrackIDs(pi.clientId, pi.entityId, pi.steamId);

			//track player current playfield / position
			mPlayerCurPlayFields.Add(pi.entityId, pi.playfield);
			mPlayerPosNRots.Add(pi.entityId, new IdPositionRotation(pi.entityId, pi.pos, pi.rot));

			if(!mbClanTyadaCreated)
			{
				//make faction
				string	makeFact	="remoteex cl=" + pi.clientId + " 'faction create Tyada'";
				mGameAPI.Console_Write("Attempting faction create command: " + makeFact);
				mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new PString(makeFact));
				mbClanTyadaCreated	=true;
			}
			else
			{
				//put them in the clan
				string	joinFact	="remoteex cl=" + pi.clientId + " 'faction join Tyada " + pi.playerName + "'";
				mGameAPI.Console_Write("Attempting faction join command: " + joinFact);
				mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new PString(joinFact));
			}
		}


		void JoinCastae(PlayerInfo pi)
		{
			mGameAPI.Console_Write("Player " + pi.playerName + " joins Castae...");
			AttentionMessage(pi.playerName + " has joined Clan Castae!");
			mClanCastae.Add(pi);
			TrackIDs(pi.clientId, pi.entityId, pi.steamId);

			//track player current playfield
			mPlayerCurPlayFields.Add(pi.entityId, pi.playfield);
			mPlayerPosNRots.Add(pi.entityId, new IdPositionRotation(pi.entityId, pi.pos, pi.rot));

			if(!mbClanCastaeCreated)
			{
				//good time to make the factions
				string	makeFact	="remoteex cl=" + pi.clientId + " faction create Castae";
				mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new PString(makeFact));
				mbClanCastaeCreated	=true;
			}

			//put them in the clan
			string	joinFact	="remoteex cl=" + pi.clientId + " faction join Castae " + pi.playerName;
			mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new PString(joinFact));
		}


		void HandlePlayerInfo(PlayerInfo pi)
		{
			if(pi == null)
			{
				return;
			}

			//dead tracking?
			if(bIsDeadListed(pi))
			{
				HandleDead(pi);
			}

			//if the match already started, new players joining should
			//not be allowed to interfere.  Observer would be ideal
			//maybe add them to a powerless faction?
			if(mbMatchStarted)
			{
				//check for disconnected players rejoining
				if(bInCastaeDiscos(pi))
				{
					mGameAPI.Console_Write("Player " + pi.playerName + " rejoin to Castae...");
					AttentionMessage(pi.playerName + " has returned to Clan Castae!");
					mCastaeDiscos.Remove(pi.steamId);
					mClanCastae.Add(pi);
					TrackIDs(pi.clientId, pi.entityId, pi.steamId);
				}
				else if(bInTyadaDiscos(pi))
				{
					mGameAPI.Console_Write("Player " + pi.playerName + " rejoin to Tyada...");
					AttentionMessage(pi.playerName + " has returned to Clan Tyada!");
					mTyadaDiscos.Remove(pi.steamId);
					mClanTyada.Add(pi);
					TrackIDs(pi.clientId, pi.entityId, pi.steamId);
				}
				else
				{
					if(!bInClanCastae(pi) && !bInClanTyada(pi))
					{
						mGameAPI.Console_Write("Player " + pi.playerName + " late join...");
						AttentionMessage(pi.playerName + " has joined too late!");
					}
				}
				return;
			}

			int	teamSize	=mIConstants["TeamSize"];

			if(pi.startPlayfield == "Castae")
			{
				if(!bInClanCastae(pi))
				{
					if(mClanCastae.Count >= teamSize)
					{
						mGameAPI.Console_Write("Player " + pi.playerName + " attempting to join Castae, but it is full!");
						if(mClanTyada.Count < teamSize)
						{
							JoinTyada(pi);
							PortPlayerHome(pi, "Tyada");
						}
						else
						{
							mGameAPI.Console_Write("Both teams full yet the game hasn't started... No idea how this could happen...");
						}
					}
					else
					{
						JoinCastae(pi);
					}
				}
			}
			else if(pi.startPlayfield == "Tyada")
			{
				if(!bInClanTyada(pi))
				{
					if(mClanTyada.Count >= teamSize)
					{
						mGameAPI.Console_Write("Player " + pi.playerName + " attempting to join Tyada, but it is full!");

						if(mClanCastae.Count < teamSize)
						{
							JoinCastae(pi);
							PortPlayerHome(pi, "Castae");
						}
						else
						{
							mGameAPI.Console_Write("Both teams full yet the game hasn't started... No idea how this could happen...");
						}
					}
					else
					{
						JoinTyada(pi);
					}
				}
			}
			else
			{
				if(!bInClanCastae(pi) && !bInClanTyada(pi))
				{
					mGameAPI.Console_Write("Player " + pi.playerName + " unclanned join...");
					AttentionMessage(pi.playerName + " started in an odd location, no clans for you!");
				}
			}

			if(bMatchReadyToStart() && !mbMatchStarted)
			{
				mGameAPI.Console_Write("Player " + pi.playerName + " causing match start...");
				MatchStart();
			}
			else
			{
				ReportPlayersNeeded();
			}
		}


		void CloseEmptyPlayField(string pfName)
		{
			PlayFieldTimer	pfTimer	=new PlayFieldTimer(
				mIConstants["EmptyPlayFieldShutOffSeconds"] * 1000, pfName);

			pfTimer.Elapsed	+=OnPlayFieldCloseTimer;
			pfTimer.Start();

			mPlayFieldDeSpawnTimers.Add(pfName, pfTimer);
		}


		void ReportPlayersNeeded()
		{
			int	tyCount		=mClanTyada.Count;
			int	casCount	=mClanCastae.Count;
			int	teamSize	=mIConstants["TeamSize"];

			string	rep	="";

			if(tyCount == teamSize)
			{
				rep	+="Clan Tyada is full, ";
			}
			else
			{
				rep	+="Clan Tyada needs " + (teamSize - tyCount) + " more soldiers, ";
			}

			if(casCount == teamSize)
			{
				rep	+="Castae is full.";
			}
			else
			{
				rep	+="Castae needs " + (teamSize - casCount) + " more for the match to begin.";
			}

			ChatMessage(rep);			
		}
		

		void HandleGlobalStructureList(GlobalStructureList gsl)
		{
			if(gsl == null)
			{
				return;
			}

			foreach(KeyValuePair<string, List<GlobalStructureInfo>> pfstructs in gsl.globalStructures)
			{
				mGameAPI.Console_Write("GSL for " + pfstructs.Key);
				foreach(GlobalStructureInfo gsi in pfstructs.Value)
				{
//					mGameAPI.Console_Write("Name: " + gsi.name);
					if(gsi.name == "Tyada Rally")
					{
						MakeStartPositions(mStartPositionsTyada, gsi.pos);
						mbStartsRecordedTyada	=true;
						mTyadaRallyID			=gsi.id;
					}
					else if(gsi.name == "Castae Rally")
					{
						MakeStartPositions(mStartPositionsCastae, gsi.pos);
						mbStartsRecordedCastae	=true;
						mCastaeRallyID			=gsi.id;
					}
					else if(gsi.name == "Temple of Styx")
					{
						if(pfstructs.Key == "Castae")
						{
							mCastaeTempleID		=gsi.id;
							mStyx.SetCastaeTemplePos(gsi.pos);
						}
						else if(pfstructs.Key == "Tyada")
						{
							mTyadaTempleID	=gsi.id;
							mStyx.SetTyadaTemplePos(gsi.pos);
						}
					}
				}
			}
		}


		void HandleSacrificeItems(ItemExchangeInfo iei)
		{
			if(iei == null)
			{
				return;
			}

			if(!mPlayerEntIDToClientID.ContainsKey(iei.id))
			{
				mGameAPI.Console_Write("Player " + iei.id + " got itemExchange event but has no clientID recorded!");
				return;
			}

			int	cid	=mPlayerEntIDToClientID[iei.id];
			if(!mPlayerCIDToSteamID.ContainsKey(cid))
			{
				mGameAPI.Console_Write("Player " + cid + " got itemExchange event but has no steamID recorded!");
				return;
			}

			if(!mPlayersSacrificing.Contains(iei.id))
			{
				mGameAPI.Console_Write("Player " + iei.id + " got itemExchange event but isn't sacrificing... Maybe another mod sent this event.");
			}
			else
			{
				mStyx.SacrificeItems(mPlayerCIDToSteamID[cid], iei.items);
				mPlayersSacrificing.Remove(iei.id);

				//for debugging prices and such
				foreach(ItemStack st in iei.items)
				{
					mGameAPI.Console_Write("Item: " + st.id + ", " + st.count);
				}

				//set the timestamp
				if(mPlayerSacTimes.ContainsKey(iei.id))
				{
					mPlayerSacTimes[iei.id]	=DateTime.Now;
				}
				else
				{
					mPlayerSacTimes.Add(iei.id, DateTime.Now);
				}
			}
		}


		void HandleDisconnect(int id)
		{
			{
				PlayerInfo	casResult	=mClanCastae.FirstOrDefault(e => e.entityId == id);
				if(casResult != null)
				{
					mClanCastae.Remove(casResult);
					mCastaeDiscos.Add(casResult.steamId);
					AttentionMessage(casResult.playerName + " has left the match from Clan Castae!  They can rejoin before the match ends.");
					mGameAPI.Console_Write("Player " + casResult.playerName + " disco castae...");

					UnTrackIDs(casResult.clientId, casResult.entityId, casResult.steamId);
				}
			}

			{
				PlayerInfo	tyResult	=mClanTyada.FirstOrDefault(e => e.entityId == id);
				if(tyResult != null)
				{
					mClanTyada.Remove(tyResult);
					mTyadaDiscos.Add(tyResult.steamId);
					AttentionMessage(tyResult.playerName + " has left the match from Clan Tyada!  They can rejoin before the match ends.");
					mGameAPI.Console_Write("Player " + tyResult.playerName + " disco tyada...");

					UnTrackIDs(tyResult.clientId, tyResult.entityId, tyResult.steamId);
				}
			}
		}


		void MatchStart()
		{
			mbMatchStarted	=true;

			mSecondRemaining	=16;

			mCountDownTimer	=new Timer(1000);

			mCountDownTimer.AutoReset	=true;
			mCountDownTimer.Elapsed		+=OnCountDown;
			mCountDownTimer.Start();

			//start the timer that gathers info on all player's locations
			mPosRotTimer			=new Timer(mIConstants["PosAndRotUpdateInterval"]);
			mPosRotTimer.AutoReset	=true;
			mPosRotTimer.Elapsed	+=OnPosAndRotTimer;
			mPosRotTimer.Start();
		}


		void UnlockDoors()
		{
			mGameAPI.Console_Write("Unlocking doors, IDs are: " +
				mTyadaRallyID + ", " + mCastaeRallyID + ", " +
				mTyadaTempleID + ", " + mCastaeTempleID);

			//tyada door
			string	doDoors	="remoteex pf=" + mTyadaPFID + " setdevicespublic " + mTyadaRallyID + " Door";
			mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new PString(doDoors));

			mGameAPI.Console_Write("Unlocking Tyada Door with: " + doDoors);

			//castae door
			doDoors	="remoteex pf=" + mCastaePFID + " setdevicespublic " + mCastaeRallyID + " Door";
			mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new PString(doDoors));

			mGameAPI.Console_Write("Unlocking Castae Door with: " + doDoors);
		}


		void KillTimer(int entID)
		{
			Timer	t	=null;
			foreach(KeyValuePair<Timer, int> tmr in mActiveTimers)
			{
				if(tmr.Value == entID)
				{
					t	=tmr.Key;
					break;
				}
			}

			if(t == null)
			{
				mGameAPI.Console_Write("Timer is null in KillTimer()");
				return;
			}

			mActiveTimers.Remove(t);
			t.Elapsed	-=OnTimerDone;	//don't leak

			t.Close();
		}


		void AddDead(PlayerInfo pid)
		{
			if(mDeadPlayers.Where(e => e.mEntID == pid.entityId).Count() > 0)
			{
				//shouldn't happen
				mGameAPI.Console_Write("Player " + pid.playerName + " died twice really fast I guess.");
				return;
			}

			Deadites	d	=new Deadites();

			d.mEntID	=pid.entityId;
			d.mPos		=pid.pos;

			if(pid.pos.x == 0.0f && pid.pos.y == 0.0f && pid.pos.z == 0.0f)
			{
				mGameAPI.Console_Write("Player " + pid.playerName + " stage 0.");
			}
			else
			{
				mGameAPI.Console_Write("Player " + pid.playerName + " stage 1.");
			}

			mDeadPlayers.Add(d);
		}


		void TrackDeadPlayers(PlayerInfo pid)
		{
			if(mActiveTimers.ContainsValue(pid.entityId))
			{
				//no need to do a timer, probably got here via
				//a player_info request from another mod
				return;
			}

			mGameAPI.Console_Write("Player " + pid.playerName + " dead tracking...");

			//watch for respawn
			Timer	tm	=new Timer(MoveTestInterval);

			tm.Elapsed	+=OnTimerDone;

			mActiveTimers.Add(tm, pid.entityId);

			tm.Start();
		}


		void Resurrect(int entID, int playerExp, string curPlayField, PVector3 curPos)
		{
			PlayerInfo	casResult	=mClanCastae.FirstOrDefault(e => e.entityId == entID);
			PlayerInfo	tyResult	=mClanTyada.FirstOrDefault(e => e.entityId == entID);

			if(casResult == null && tyResult == null)
			{
				return;
			}

			if(casResult != null)
			{
				casResult.exp	=playerExp;	//update exp value
				Resurrect(casResult, "Castae", curPlayField, curPos);
			}
			else
			{
				tyResult.exp	=playerExp;	//update exp value
				Resurrect(tyResult, "Tyada", curPlayField, curPos);
			}
		}


		void Resurrect(PlayerInfo pi, string homePlayField, string curPlayField, PVector3 curPos)
		{
			PrivateMessage(pi.entityId, "You have been reassembled by the Queen at a cost of " + pi.exp + "...");

			mStyx.DeductResCost(pi.steamId, pi.exp);

			List<PVector3>	starts	=(homePlayField == "Castae")? mStartPositionsCastae : mStartPositionsTyada;

			//if player is within a meter of the start locations, they probably
			//just chose to respawn there anyway, so no need to teleport
			foreach(PVector3 spots in starts)
			{
				float	dist	=VecStuff.Distance(spots, curPos);

//				mGameAPI.Console_Write("Check for near rally: " + VectorToString(spots) + ", " + VectorToString(curPos) + ", " + dist);

				if(dist < SpawnDetectDistanceMax)
				{
					mGameAPI.Console_Write("Player " + pi.playerName + " chose to spawn at rally anyway...  Good player!");
					return;
				}
			}

			int	randSpot	=mRand.Next(0, starts.Count);

			if(curPlayField == homePlayField)
			{
				IdPositionRotation	idpr	=new IdPositionRotation(pi.entityId, starts[randSpot], new PVector3());
				mGameAPI.Console_Write("Player " + pi.playerName + " being moved to rally from current planet...");
				mGameAPI.Game_Request(CmdId.Request_Entity_Teleport,
					(ushort)CmdId.Request_Entity_Teleport,
					idpr);
			}
			else
			{
				PVector3	pos	=starts[randSpot];

				IdPlayfieldPositionRotation	ipfpr	=new IdPlayfieldPositionRotation(pi.entityId, homePlayField, pos, new PVector3());

				mGameAPI.Console_Write("Player " + pi.playerName + " being moved to rally from offworld...");
				mGameAPI.Game_Request(CmdId.Request_Player_ChangePlayerfield, (ushort)CmdId.Request_Player_ChangePlayerfield, ipfpr);
			}
		}


		void PortPlayerHome(PlayerInfo pi, string homePlayField)
		{
			List<PVector3>	starts	=(homePlayField == "Castae")? mStartPositionsCastae : mStartPositionsTyada;

			int	randSpot	=mRand.Next(0, starts.Count);

			PVector3	pos	=starts[randSpot];

			IdPlayfieldPositionRotation	ipfpr	=new IdPlayfieldPositionRotation(pi.entityId, homePlayField, pos, new PVector3());

			mGameAPI.Console_Write("Player " + pi.playerName + " being moved to rally from offworld to facilitate a team switch...");
			mGameAPI.Game_Request(CmdId.Request_Player_ChangePlayerfield, (ushort)CmdId.Request_Player_ChangePlayerfield, ipfpr);
		}


		bool bMatchReadyToStart()
		{
//			return	true;	//for testing
			return	(mClanCastae.Count == mIConstants["TeamSize"] && mClanTyada.Count == mIConstants["TeamSize"]);
		}


		bool bIsDeadListed(PlayerInfo pi)
		{
			return	(mDeadPlayers.Where(e => e.mEntID == pi.entityId).Count() != 0);
		}


		bool bInClanCastae(PlayerInfo pi)
		{
			return	bInClanCastae(pi.entityId);
		}


		bool bInClanCastae(int entID)
		{
			return	(mClanCastae.Where(e => e.entityId == entID).Count() != 0);
		}


		bool bInClanTyada(PlayerInfo pi)
		{
			return	(mClanTyada.Where(e => e.entityId == pi.entityId).Count() != 0);
		}


		bool bInCastaeDiscos(PlayerInfo pi)
		{
			return	(mCastaeDiscos.Where(e => e == pi.steamId).Count() != 0);
		}


		bool bInTyadaDiscos(PlayerInfo pi)
		{
			return	(mTyadaDiscos.Where(e => e == pi.steamId).Count() != 0);
		}


		void RemoveDead(int entID)
		{
			Deadites	d	=mDeadPlayers.FirstOrDefault(e => e.mEntID == entID);
			if(d != null)
			{
				mDeadPlayers.Remove(d);
			}
		}


		void MakeStartPositions(List<PVector3> sPos, PVector3 structurePos)
		{
			for(int i=0;i < 5;i++)
			{
				sPos.Add(VecStuff.Add(mVConstants["PadOffset0" + i], structurePos));
			}
		}


		//return true if it's ok to ask for more goodies
		bool	CheckStyxSpamInterval(int playerEntID)
		{
			if(!mPlayerSacTimes.ContainsKey(playerEntID))
			{
//				mGameAPI.Console_Write("No sac time recorded for player");
				return	true;
			}
			TimeSpan	since	=DateTime.Now - mPlayerSacTimes[playerEntID];
//			mGameAPI.Console_Write("Player last sacrificed : " + since.ToString());
			if(since.TotalSeconds < mIConstants["StyxAskForMoreInterval"])
			{
				return	false;
			}
			return	true;
		}


		void CheckStyx(int entID)
		{
			if(mPlayerCurPlayFields.ContainsKey(entID))
			{
				if(mPlayerCurPlayFields[entID] == "Tyada")
				{
					if(mStyx.bCheckNear(mPlayerPosNRots[entID].pos, true))
					{
//						mGameAPI.Console_Write("Player within range of Styx");
						if(!mPlayersSacrificing.Contains(entID))
						{
//							mGameAPI.Console_Write("Player not already sacrificing");

							//make sure styx doesn't greedily spam
							if(CheckStyxSpamInterval(entID))
							{
//								mGameAPI.Console_Write("Player ready to fork over goodies");
								mPlayersSacrificing.Add(entID);
								SacrificeItems(entID);
							}
						}
					}
				}
				else if(mPlayerCurPlayFields[entID] == "Castae")
				{
					if(mStyx.bCheckNear(mPlayerPosNRots[entID].pos, false))
					{
						if(!mPlayersSacrificing.Contains(entID))
						{
							//make sure styx doesn't greedily spam
							if(CheckStyxSpamInterval(entID))
							{
								mPlayersSacrificing.Add(entID);
								SacrificeItems(entID);
							}
						}
					}
				}
			}
		}


		void SacrificeItems(int entID)
		{
			ItemExchangeInfo	iei	=new ItemExchangeInfo(entID, "Sacrifice", "Bring forth your sacrifice and you shall be richly rewarded!", "Give", null);
			mGameAPI.Game_Request(CmdId.Request_Player_ItemExchange, 0, iei);
		}


		void HandleDead(PlayerInfo pi)
		{
			Deadites	deader	=mDeadPlayers.FirstOrDefault(e => e.mEntID == pi.entityId);

			//printing stuff trying to find some sort of "player just spawned" indicator
			mGameAPI.Console_Write("Dead Tracking: " + pi.health + ", exp:" + pi.exp + ", " +
				VecStuff.ToString(pi.pos) + ", " + VecStuff.ToString(deader.mPos));

			if(deader.mPos.x == 0f && deader.mPos.y == 0f && deader.mPos.z == 0f)
			{
				//when the player dies, the position is invalid, so this will
				//just update that position.
				if(pi.pos.x == 0.0f && pi.pos.y == 0.0f && pi.pos.z == 0.0f)
				{
					//still stage 0
					mGameAPI.Console_Write("Still no valid position for deader " + pi.playerName);
				}
				else
				{
					deader.mPos	=pi.pos;
					mGameAPI.Console_Write(pi.playerName + " got a valid position");
				}

				//restart timer
				TrackDeadPlayers(pi);
				return;
			}

			//check for movement
			//if there's a large move, it probably means a spawn point was chosen
			//different from the "spawn nearby" option
			float	dist	=VecStuff.Distance(pi.pos, deader.mPos);
			if(dist > SpawnDetectDistanceMax)
			{
				//big jump means spawn point chosen
				//copy the position and wait a tic
				deader.mPos	=pi.pos;
				mGameAPI.Console_Write("Big jump detected, waiting a tic");

				TrackDeadPlayers(pi);
				return;
			}

			if(dist > SpawnDetectDistanceMin)
			{
				//small movements should mean the player is loaded and moving
				mGameAPI.Console_Write(pi.playerName + " moved a small amount, hoping they have spawned");

				//can stop tracking
				RemoveDead(pi.entityId);

				//kill timer too in case this event was triggered by another mod
				KillTimer(pi.entityId);

				//player has respawned (we hope)
				//teleport them to rally and deduct score
				Resurrect(pi.entityId, pi.exp, pi.playfield, pi.pos);
			}
			else
			{
				TrackDeadPlayers(pi);
			}
		}


 		void LoadConstants()
		{
			//can't be too sure of what the current directory is
			Assembly	ass	=Assembly.GetExecutingAssembly();

			string	dllDir	=Path.GetDirectoryName(ass.Location);

			string	filePath	=Path.Combine(dllDir, "Constants.txt");

			mGameAPI.Console_Write("Loading Config file for constants: " + filePath);

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
				if(line == "")
				{
					//skip blank lines
					continue;
				}

				string	[]toks	=line.Split(' ', '\t');

				if(toks.Length < 2)
				{
					//bad line
					mGameAPI.Console_Write("Bad line in constants config file at position: " + sr.BaseStream.Position);
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

				string	cname	=toks[idx];
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

				//check for vector
				if(toks[idx].StartsWith("("))
				{
					string	sansParen	=toks[idx].Substring(1);

					float	fval0	=0f;
					if(!float.TryParse(sansParen, out fval0))
					{
						mGameAPI.Console_Write("Bad token:" + sansParen + ", looking for vector value in constants config file at position: " + sr.BaseStream.Position);
						continue;
					}
					idx++;

					float	fval1	=0f;
					if(!float.TryParse(toks[idx], out fval1))
					{
						mGameAPI.Console_Write("Bad token:" + toks[idx] + ", looking for vector value in constants config file at position: " + sr.BaseStream.Position);
						continue;
					}
					idx++;

					sansParen	=toks[idx].Substring(0, toks[idx].Length - 1);

					float	fval2	=0f;
					if(!float.TryParse(sansParen, out fval2))
					{
						mGameAPI.Console_Write("Bad token:" + sansParen + ", looking for vector value in constants config file at position: " + sr.BaseStream.Position);
						continue;
					}

					PVector3	vecVal	=new PVector3(fval0, fval1, fval2);
					mVConstants.Add(cname, vecVal);
				}
				else
				{
					int	val	=0;
					if(!int.TryParse(toks[idx], out val))
					{
						mGameAPI.Console_Write("Bad token looking for value in constants config file at position: " + sr.BaseStream.Position);
						continue;
					}

					mIConstants.Add(cname, val);
				}
			}

			sr.Close();
			fs.Close();

			foreach(KeyValuePair<string, int> vals in mIConstants)
			{
				mGameAPI.Console_Write("Const: " + vals.Key + ", " + vals.Value);
			}
		}


		#region Chat Stuff
		void PrivateMessage(int entID, string msg)
		{
			string	cmd	="say p:" + entID + " '" + msg + "'";
            mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new Eleon.Modding.PString(cmd));
		}


		void ChatMessage(String msg)
        {
            String command = "SAY '" + msg + "'";
            mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new Eleon.Modding.PString(command));
        }


		void TyadaChat(string msg)
		{
			string	cmd	="SAY f:Tyada '" + msg + "'";
            mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new Eleon.Modding.PString(cmd));
		}


		void CastaeChat(string msg)
		{
			string	cmd	="SAY f:Castae '" + msg + "'";
            mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new Eleon.Modding.PString(cmd));
		}


        void NormalMessage(String msg)
        {
            mGameAPI.Game_Request(CmdId.Request_InGameMessage_AllPlayers, 0, new IdMsgPrio(0, msg, 0, 100));
        }


        void AlertMessage(String msg)
        {
            mGameAPI.Game_Request(CmdId.Request_InGameMessage_AllPlayers, 0, new IdMsgPrio(0, msg, 1, 100));
        }


        void AttentionMessage(String msg)
        {
            mGameAPI.Game_Request(CmdId.Request_InGameMessage_AllPlayers, (ushort)CmdId.Request_InGameMessage_AllPlayers, new IdMsgPrio(0, msg, 2, 100));
        }
		#endregion


		#region Event Handlers
		void OnPosAndRotTimer(Object src, ElapsedEventArgs eea)
		{
//			mGameAPI.Console_Write("PosAndRot timer tick");

			foreach(PlayerInfo pi in mClanCastae)
			{
				//skip deadites
				if(mDeadPlayers.Where(d => d.mEntID == pi.entityId).Count() > 0)
				{
					continue;
				}

				mGameAPI.Game_Request(CmdId.Request_Entity_PosAndRot, 0, new Id(pi.entityId));
			}

			foreach(PlayerInfo pi in mClanTyada)
			{
				//skip deadites
				if(mDeadPlayers.Where(d => d.mEntID == pi.entityId).Count() > 0)
				{
					continue;
				}

				mGameAPI.Game_Request(CmdId.Request_Entity_PosAndRot, 0, new Id(pi.entityId));
			}
		}


		void OnCountDown(Object src, ElapsedEventArgs eea)
		{
			mSecondRemaining--;

			if(mSecondRemaining > 0)
			{
				AlertMessage("Match begins in " + mSecondRemaining + "...");
				return;
			}

			AlertMessage("Fight for the glory of Queen Styx!");

			UnlockDoors();
			mStyx.StartMatch(mIConstants["MatchDurationMinutes"], mClanTyada, mClanCastae);

			mCountDownTimer.Stop();
		}


		void OnStarterPlayFieldTimeOut(Object src, ElapsedEventArgs eea)
		{
			if(mbStartsRecordedCastae && mbStartsRecordedTyada)
			{
				return;
			}

			if(mbRequestedTyadaLoad && !mbStartsRecordedTyada)
			{
				mGameAPI.Console_Write("Trying again to load starter Tyada");
				int	pid	=mPlayFieldServers[0].Id;

				//use 2 to load the starter planets and keep them loaded
				PlayfieldLoad	pfTy	=new PlayfieldLoad(float.MaxValue, "Tyada", pid);
				mGameAPI.Game_Request(CmdId.Request_Load_Playfield, (ushort)pid, pfTy);
				mbRequestedTyadaLoad	=true;

				mStarterLoadTimer.Start();
			}
		}


		void OnGameDataTimer(Object src, ElapsedEventArgs eea)
		{
			if(mbStartsRecordedCastae && mbStartsRecordedTyada)
			{
				mGameDataTimer.Stop();
				return;
			}
			mGameAPI.Game_Request(CmdId.Request_Playfield_List, 0, null);

			if(mPlayFieldServers.Count == mIConstants["NumReservedPlayFields"])
			{
				if(!mbRequestedTyadaLoad)
				{
					int	pid	=mPlayFieldServers[0].Id;
					mGameAPI.Console_Write("Loading starter Tyada: " + pid);

					//use 2 to load the starter planets and keep them loaded
					PlayfieldLoad	pfTy	=new PlayfieldLoad(float.MaxValue, "Tyada", pid);
					mGameAPI.Game_Request(CmdId.Request_Load_Playfield, (ushort)pid, pfTy);
					mbRequestedTyadaLoad	=true;
					mTyadaPFID				=pid;

					//the above command often gets ignored, retry might be needed
					mStarterLoadTimer			=new Timer(mIConstants["StartPlayFieldsRetrySeconds"] * 1000);
					mStarterLoadTimer.Elapsed	+=OnStarterPlayFieldTimeOut;
					mStarterLoadTimer.Start();
				}
				else if(mbStartsRecordedTyada && !mbRequestedCastaeLoad)
				{
					int	pid	=mPlayFieldServers[1].Id;
					mGameAPI.Console_Write("Loading starter Castae: " + pid);

					//we know tyada is loaded at this point
					PlayfieldLoad	pfCas	=new PlayfieldLoad(float.MaxValue, "Castae", pid);
					mGameAPI.Game_Request(CmdId.Request_Load_Playfield, (ushort)pid, pfCas);
					mbRequestedCastaeLoad	=true;
					mCastaePFID				=pid;
				}
			}
			else
			{
				Process	[]procs	=Process.GetProcesses();
				foreach(Process pc in procs)
				{
					//mGameAPI.Console_Write("Process: " + pc.ProcessName);

					if(pc.ProcessName == "EmpyrionPlayfieldServer")
					{
						if(!bHasPFProcess(pc))
						{
							mGameAPI.Console_Write("Adding process " + pc.Id + " to list of reserved.");
							mPlayFieldServers.Add(pc);
						}
					}
				}
			}
		}


		void OnPlayFieldCloseTimer(Object sender, ElapsedEventArgs eea)
		{
			PlayFieldTimer	pft	=sender as PlayFieldTimer;

			mGameAPI.Console_Write("Closing " + pft.mPlayField);

			string	cmd	="stoppf '" + pft.mPlayField + "'";
            mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new Eleon.Modding.PString(cmd));

			pft.Stop();
			mPlayFieldDeSpawnTimers.Remove(pft.mPlayField);
		}


		void OnTimerDone(Object src, ElapsedEventArgs eea)
		{
			Timer	ticker	=src as Timer;
			if(ticker == null)
			{
				mGameAPI.Console_Write("Ticker null in OnTimerDone!");
				return;
			}

			if(mActiveTimers.ContainsKey(ticker))
			{
				int	entID	=mActiveTimers[ticker];

				mActiveTimers.Remove(ticker);
				ticker.Elapsed	-=OnTimerDone;	//don't leak

				ticker.Close();

				Id	pid	=new Id(entID);

				mGameAPI.Console_Write("Timer tick for player id: " + entID);

				if((mClanCastae.Where(e => e.entityId == entID).Count() == 0)
					&& (mClanTyada.Where(e => e.entityId == entID).Count() == 0))
				{
					//player isn't even clanned, maybe ragequit?
					mGameAPI.Console_Write("Unclanned player in OnTimerDone()");
					RemoveDead(entID);
					return;
				}

				//watch for player health to return
                mGameAPI.Game_Request(CmdId.Request_Player_Info, (ushort)CmdId.Request_Player_Info, pid);
			}
		}


		void OnDebugSpew(object sender, EventArgs ea)
		{
			string	spew	=sender as string;
			if(spew == null)
			{
				return;
			}
            mGameAPI.Console_Write(spew);
		}


		void OnStyxGameEnd(Object sender, EventArgs ea)
		{
			string	cmd	="saveandexit 1";

			mGameAPI.Game_Request(CmdId.Request_ConsoleCommand, 0, new Eleon.Modding.PString(cmd));
		}


		void OnStyxReturnItems(Object sender, EventArgs ea)
		{
			ItemReturnEventArgs	irea	=ea as ItemReturnEventArgs;
			if(irea == null || irea.mItems == null)
			{
				mGameAPI.Console_Write("Null item stack in OnStyxReturnItems!");
				return;
			}

			if(!mPlayerSteamIDToEntID.ContainsKey(irea.mPlayerSteamID))
			{
				mGameAPI.Console_Write("No steam id to entity id conversion for steam id " + irea.mPlayerSteamID + " in OnStyxReturnItems!");
				return;
			}

			int	eid	=mPlayerSteamIDToEntID[irea.mPlayerSteamID];

			foreach(ItemStack ist in irea.mItems)
			{
				mGameAPI.Game_Request(CmdId.Request_Player_AddItem, 0, new IdItemStack(eid, ist));
			}
		}


		void OnStyxSpeakToAll(Object sender, EventArgs ea)
		{
			SpeakEventArgs	sea	=ea as SpeakEventArgs;
			if(sea == null)
			{
				mGameAPI.Console_Write("Null SpeakEventArgs in OnStyxSpeakToAll!");
				return;
			}

			if(sea.mbAlertMsg)
			{
				AlertMessage(sea.mMsg);
			}
			else
			{
				ChatMessage(sea.mMsg);
			}
		}


		void OnStyxSpeakToPlayer(Object sender, EventArgs ea)
		{
			SpeakEventArgs	sea	=ea as SpeakEventArgs;
			if(sea == null)
			{
				mGameAPI.Console_Write("Null SpeakEventArgs in OnStyxSpeakToPlayer!");
				return;
			}

			if(!mPlayerSteamIDToEntID.ContainsKey(sea.mPlayerSteamID))
			{
				mGameAPI.Console_Write("No steam id to entity id conversion for steam id " + sea.mPlayerSteamID + " in OnStyxReturnItems!");
				return;
			}

			int	eid	=mPlayerSteamIDToEntID[sea.mPlayerSteamID];

			mGameAPI.Console_Write("Styx speaking to player: " + sea.mPlayerSteamID + " message: " + sea.mMsg);

			PrivateMessage(eid, sea.mMsg);
		}


		void OnStyxSpeakToPlayerClan(Object sender, EventArgs ea)
		{
			SpeakEventArgs	sea	=ea as SpeakEventArgs;
			if(sea == null)
			{
				mGameAPI.Console_Write("Null SpeakEventArgs in OnStyxSpeakToPlayer!");
				return;
			}

			if(!mPlayerSteamIDToClientID.ContainsKey(sea.mPlayerSteamID))
			{
				mGameAPI.Console_Write("No steam id to client id conversion for steam id " + sea.mPlayerSteamID + " in OnStyxReturnItems!");
				return;
			}

			int	cid	=mPlayerSteamIDToClientID[sea.mPlayerSteamID];

			PlayerInfo	casResult	=mClanCastae.FirstOrDefault(e => e.clientId == cid);
			if(casResult != null)
			{
				CastaeChat(sea.mMsg);
				return;
			}

			PlayerInfo	tyResult	=mClanTyada.FirstOrDefault(e => e.clientId == cid);
			if(tyResult != null)
			{
				TyadaChat(sea.mMsg);
				return;
			}

			mGameAPI.Console_Write("Styx trying to speak to a player's clan when the player is not in one of the clans!");
		}
		#endregion
	}
}