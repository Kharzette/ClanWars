# ClanWars

Clone https://github.com/lostinplace/sample-empyrion-mod.git and add this project to the solution.  You'll need to fix up the paths to the missing references to point to the sample project's dependencies directory.

Copy the built ClanWarsModule.dll and text files to your server's mod folder in a directory called ClanWars (or whatever).

The mod does a simple match of teamsize vs teamsize players where contributions to an AI queen (in poi on both starter worlds) rack up a score.  The best score at the end wins.

Most all parameters are adjustable via the text files.

If nobody ends up playing this, the source should still be useful for examples of how to message, set up guilds and playfields automagically, unlock doors, and hackily mess with death and respawn.