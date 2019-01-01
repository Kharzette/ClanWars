using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eleon.Modding;


namespace ClanWarsModule
{
	internal class AIQueen
	{
		//team tyada ingots contributed
		int	mTIron, mTCopper, mTCobalt;
		int	mTNeodymium, mTSilicon, mTSathium;
		int	mTErestrum, mTZasconium;

		//team castae ingots contributed
		int	mCIron, mCCopper, mCCobalt;
		int	mCNeodymium, mCSilicon, mCSathium;
		int	mCErestrum, mCZasconium;

		PVector3	mTyadaPos, mCastaePos;

		float		mNearDistance;	//how far away to contribute ingotses?


		internal AIQueen(float nearDist)
		{
			mNearDistance	=nearDist;
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
	}
}
