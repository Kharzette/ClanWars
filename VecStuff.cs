using System;
using Eleon.Modding;


namespace ClanWarsModule
{
	internal static class VecStuff
	{
		internal static PVector3 AdjustUp(PVector3 pos)
		{
			PVector3	ret	=pos;

			ret.y	+=0.1f;

			return	ret;
		}


		internal static string ToString(PVector3 vec)
		{
			return	"( " + vec.x + ", " + vec.y + ", " + vec.z + " )";
		}


		internal static float Length(PVector3 vec)
		{
			float	lenSQ	=(vec.x * vec.x) +
				(vec.y * vec.y) + (vec.z * vec.z);

			return	(float)Math.Sqrt(lenSQ);
		}


		internal static float Distance(PVector3 vecA, PVector3 vecB)
		{
			vecA.x	-=vecB.x;
			vecA.y	-=vecB.y;
			vecA.z	-=vecB.z;

			return	Length(vecA);
		}


		internal static PVector3 Add(PVector3 vecA, PVector3 vecB)
		{
			PVector3	ret;

			ret.x	=vecA.x + vecB.x;
			ret.y	=vecA.y + vecB.y;
			ret.z	=vecA.z + vecB.z;

			return	ret;
		}
	}
}
