"""SurvivorCommunity's voiced lines, and a check of the voice folders (Resources/Voices).

Two sets of lines, each clip Resources/Voices/<male|female>_<Race>_<Name>/<key>.ogg, mono Ogg Vorbis, with a <key>.txt
beside it holding the words it says (for whoever records it):

1. The dialog lines (family "dialog"), something a survivor says in a talk:
   - SC's own statements (Config/dialogs.xml, their words from Config/Localization.csv), keyed by the statement id;
   - the 0-XNPCCore lines SC voices (EXTRA), keyed "<dialog>.<statement>" (ScVoice tries that form before the bare id);
   - the enclave requests' lines, which ScEncRequests picks at runtime by kind and standing (RUNTIME_FAMILIES: LineKey's
     stem_kind_band, stem_kind, stem_band and stem; and the replies' RUNTIME_FIXED lines), keyed by their Localization
     key, which ScVoice tries first of all. The statements that only hold them ({reqask} and the like: RUNTIME_STATEMENTS)
     have no clip of their own.
   A line with a name or a number in it ({tokens}) says only its token-free part (SPOKEN); SC's line panel shows the
   whole line.
2. The V2 lines ("new"), grouped in families so a recording round can run one family at a time (NEW, in this order):
   - greet    SC's own greetings, greet.<stranger|recruit|member|follower|enclave>.<n> (ScGreetings);
   - chatter  members at home talking to nobody in particular, chatter.<n> (ScGreetings);
   - taunt    raiders, taunt.<spot|search|lost|hit|killed|idle|dying|retreat>.<n> (ScTaunts);
   - xn       0-XNPCCore's say-lines, xn.<lineId> (xn.basekeep.need stands in for "I need <item> for that.");
   - ack      Voice Control's order acknowledgements, ack.<actiontype>[.<param>].<n>, the reduced set: the weapon-swap and
              formation replies are one generic family each (ack.swapweapon.<n>, ack.setformation.<n>);
   - vc       Voice Control's hard-coded lines, vc.<id>, without its billing lines.
   The xn, ack and vc words are Voice Control 3.4.01's English, exactly: ScBarks finds the clip for a line Voice Control
   is about to speak by its words. ScVoiceLines.cs is this list for the game (gencs writes it; check says when it is
   stale).

The voices are Config/blocks.xml's Voices list (two per race and sex), plus any other folder on disk.

    python -I Tools/voice_lines.py list [--new] [--family F[,F]] [--voice FOLDER] [--tsv]
        every line's key and the words it says; --new only the V2 lines; --family only those families (dialog, greet,
        chatter, taunt, xn, ack, vc); --voice only the lines that voice has no clip for yet; --tsv key<TAB>words
    python -I Tools/voice_lines.py check [--keys]
        the lines (a token with no spoken part, a key used twice), ScVoiceLines.cs, then per voice: the lines with no
        clip yet, family by family, and clips no line uses (--keys also lists the keys of a voice with no folder yet)
    python -I Tools/voice_lines.py cost
        characters per voice (about one ElevenLabs credit each): per family, the new lines, the full set, and what each
        voice still lacks
    python -I Tools/voice_lines.py sidecars [--new] [--family F[,F]] [--voice FOLDER]
        write <key>.txt (the words it says) into every voice folder that exists (--voice: that one, made if missing)
    python -I Tools/voice_lines.py gencs
        write ScVoiceLines.cs from NEW
"""
import csv
import io
import os
import re
import sys

MOD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VOICES = os.path.join(MOD, "Resources", "Voices")
CS_TABLE = os.path.join(MOD, "ScVoiceLines.cs")

# The voice table when Config/blocks.xml has no Voices property (ScConfig's default is the same list).
DEFAULT_VOICES = [
    "male_White_Vane", "male_White_David", "female_White_Jessica", "female_White_Beth",
    "male_Black_Nate", "male_Black_Desmond", "female_Black_Tiffany", "female_Black_Rene",
    "male_Asian_Jasper", "male_Asian_Julian", "female_Asian_Layla", "female_Asian_Jess",
    "male_Native_NativeM1", "male_Native_NativeM2", "female_Native_NativeF1", "female_Native_NativeF2",
]
RACES = ("White", "Black", "Asian", "Native")

# Lines of other mods that SC voices, keyed "<dialog>.<statement>": 0-XNPCCore's greeting, at the root of every talk
# (SdcsNPC_start in its Localization.csv).
EXTRA = {
    "SdcsNPC.start": "Hello friend, how can I help you?",
}

# What a voice says of a statement whose words carry tokens (generated names and numbers can't be voiced): the
# token-free part. The panel shows the whole line.
SPOKEN = {
    "scEncAboutReply": "It's not much, but it keeps the dead out and the rain off. These people are mine to look after.",
    "scEncWhereReply": "You'll want to talk to the one in charge, not me.",
    "scEncEnlisted": "Right behind you. Say the word when you want me to head back.",
    "scEncEnlistConfirm": "I could come along and watch your back for a while.",
    "scEncRecruitConfirm": "Leave my people? I've thought about it more than once. If you'll have me, I'm in.",
}

# Statements that hold only a line picked at runtime ({reqask}, {reqwait}, {reqreply}, {reqnone}; E7's {giftreply}): no
# clip of their own.
RUNTIME_STATEMENTS = {"scEncReqNothing", "scEncReqOffer", "scEncReqWaiting", "scEncReqReply", "scEncGiftReply"}
# ScEncRequests' runtime lines: every Localization key of these stems (the stem itself, or stem_...) whose row is a dialog
# statement (a UI key named alike is no line), and the fixed ones.
RUNTIME_FAMILIES = ["scEncAsk", "scEncWait", "scEncAccepted", "scEncFound", "scEncThanks", "scEncDeclined", "scEncThink",
                    "scEncCantDo"]
RUNTIME_FIXED = ["scEncReqNothingLine", "scEncReqGone", "scEncReqNotCleared", "scEncReqNotCarried",
                 "scEncGiftRefuseCap", "scEncGiftRefuseSoon",     # E7: a gift refused (the thanks are scEncThanks_gift_*)
                 # E9: an ally's answers on the radio and their fighter's hello - said outside a talk (ScEncCalls shows them
                 # as tells: no playback yet, the voicing round wires it)
                 "scEncCallFighterSaid", "scEncCallDropSaid", "scEncCallFailSaid", "scEncCallHello",
                 # E10: a raider enclave's words on the radio - its demand, paid, refused (ScEncRaids shows them as tells: text
                 # only, the voicing round wires their playback)
                 "scEncRaidDemandSaid", "scEncRaidPaidSaid", "scEncRaidRefusedSaid",
                 # E11: a Friendly or Allied group's words on the radio as it moves - lost too many, the dead next door,
                 # the console's (ScEncMoves shows them as tells: text only, the voicing round wires their playback)
                 "scEncMoveLostSaid", "scEncMoveNestSaid", "scEncMoveSaid"]

# ---------------------------------------------------------------------------------------------------- the V2 lines ----
# No names or numbers, so every voice can say them. Drafts: SC_Ideas_and_Voices_Plan.md section 4.4 (2026-11-03).

GREET = [
    # a survivor you don't know (wild)
    ("greet.stranger.1", "Easy now. I'm not looking for trouble."),
    ("greet.stranger.2", "You're alive. That's something, these days."),
    ("greet.stranger.3", "Keep your hands where I can see them, friend."),
    ("greet.stranger.4", "Hey. You on your own out here?"),
    ("greet.stranger.5", "Don't see many people who can still talk."),
    ("greet.stranger.6", "Stay back a bit, okay? Just till I know you."),
    # someone who might join (a prospect)
    ("greet.recruit.1", "You again. Still thinking about that offer?"),
    ("greet.recruit.2", "Your people doing alright?"),
    ("greet.recruit.3", "I keep hearing good things about your place."),
    ("greet.recruit.4", "Come to talk about joining up?"),
    # one of the community at home
    ("greet.member.1", "Good to see you back in one piece."),
    ("greet.member.2", "Quiet day. I'll take it."),
    ("greet.member.3", "Walls are holding. Mostly."),
    ("greet.member.4", "There's coffee. Well, there's hot water."),
    ("greet.member.5", "Welcome home, boss."),
    ("greet.member.6", "Any luck out there?"),
    # one following you
    ("greet.follower.1", "Right with you."),
    ("greet.follower.2", "Where to next?"),
    ("greet.follower.3", "Say the word."),
    ("greet.follower.4", "I've got your back."),
    # one of an enclave's people
    ("greet.enclave.1", "You're on our ground. Mind your manners."),
    ("greet.enclave.2", "Leader's inside if you're here to talk."),
    ("greet.enclave.3", "Friend of ours? Then you're welcome."),
    ("greet.enclave.4", "Don't touch what isn't yours."),
]

CHATTER = [
    ("chatter.1", "Anyone else hear that last night?"),
    ("chatter.2", "I'd kill for a hot shower."),
    ("chatter.3", "We should reinforce the east wall."),
    ("chatter.4", "Remember pizza? Real pizza?"),
    ("chatter.5", "Running low on something. There's always something."),
    ("chatter.6", "I dreamt about the old days again."),
    ("chatter.7", "Those things never sleep, do they?"),
    ("chatter.8", "Somebody check the traps."),
    ("chatter.9", "It's too quiet. I don't like it."),
    ("chatter.10", "One more day. That's the job."),
    ("chatter.11", "I miss music. Real music."),
    ("chatter.12", "Watch the tree line."),
]

TAUNT = [
    # spots you / starts attacking you
    ("taunt.spot.1", "Well, look what crawled out!"),
    ("taunt.spot.2", "Fresh meat!"),
    ("taunt.spot.3", "Drop your gear and walk away!"),
    ("taunt.spot.4", "This is our turf!"),
    ("taunt.spot.5", "Get 'em!"),
    ("taunt.spot.6", "Nowhere to run!"),
    ("taunt.spot.7", "Everything you've got is ours now!"),
    ("taunt.spot.8", "Wrong road, pal!"),
    # goes to look at a noise
    ("taunt.search.1", "Come out, come out!"),
    ("taunt.search.2", "I know you're here."),
    ("taunt.search.3", "Check the corners!"),
    ("taunt.search.4", "You can't hide forever!"),
    # loses you
    ("taunt.lost.1", "Where'd they go?"),
    ("taunt.lost.2", "Lost 'em."),
    ("taunt.lost.3", "Next time."),
    ("taunt.lost.4", "Coward ran off."),
    # hit by you
    ("taunt.hit.1", "Ah! You'll pay for that!"),
    ("taunt.hit.2", "Lucky shot!"),
    ("taunt.hit.3", "That all you got?"),
    ("taunt.hit.4", "I'm gonna make you hurt!"),
    ("taunt.hit.5", "Damn it!"),
    # killed you
    ("taunt.killed.1", "Stay down!"),
    ("taunt.killed.2", "Strip 'em."),
    ("taunt.killed.3", "Told you. Our turf."),
    ("taunt.killed.4", "Another one for the pile."),
    # nobody to fight
    ("taunt.idle.1", "Quiet out here."),
    ("taunt.idle.2", "Who's on watch?"),
    ("taunt.idle.3", "Should've hit that trader."),
    ("taunt.idle.4", "My boots are falling apart."),
    ("taunt.idle.5", "Boss better split it fair this time."),
    ("taunt.idle.6", "Heard engines last night."),
    # dies
    ("taunt.dying.1", "No... not like this..."),
    ("taunt.dying.2", "Help me..."),
    ("taunt.dying.3", "Damn you..."),
    # badly hurt
    ("taunt.retreat.1", "Fall back!"),
    ("taunt.retreat.2", "Pull out!"),
]

# 0-XNPCCore's say-lines (Voice Control 3.4.01 SayLineTexts.Lines; the last one a stand-in for "I need <item> for that.")
XN = [
    ("xn.ammo.dry", "I'm out of ammo."),
    ("xn.loot.full", "My pack is full."),
    ("xn.loot.done", "Nothing left here."),
    ("xn.harvest.done", "No more trees here."),
    ("xn.harvest.notool", "I need an axe for that."),
    ("xn.harvest.broken", "My axe is broken."),
    ("xn.mine.done", "No more rocks here."),
    ("xn.mine.notool", "I need a pickaxe for that."),
    ("xn.mine.broken", "My pickaxe is broken."),
    ("xn.clay.done", "No more clay here."),
    ("xn.clay.notool", "I need a shovel for that."),
    ("xn.clay.broken", "My shovel is broken."),
    ("xn.pick.done", "Nothing left to pick here."),
    ("xn.butcher.done", "No more meat here."),
    ("xn.butcher.notool", "I need a knife for that."),
    ("xn.basekeep.notool", "I need a hammer for that."),
    ("xn.basekeep.nobase", "There's no base here to keep."),
    ("xn.basekeep.remembered", "Base remembered."),
    ("xn.medic.firstaid", "Applying first aid."),
    ("xn.vehicle.wait", "I'll get in when you do."),
    ("xn.vehicle.noroom", "No room."),
    ("xn.vehicle.far", "I'm too far away."),
    ("xn.basekeep.need", "I need more materials for that."),
]

# Voice Control's order acknowledgements: phrasetriggers.xml's English responses of the enabled triggers (0-XNPCCore
# 3.4.01), keyed ack.<ActionType lowercased>[.<ActionParam, non-alphanumerics as _>].<n> - except the weapon swap and the
# formations, which get one generic family each at the end.
ACK = [
    ("ack.followplayer.1", "On it. I'll stick close."),
    ("ack.followplayer.2", "Alright, let's move together."),
    ("ack.followplayer.3", "Right behind you."),
    ("ack.stopfollow.1", "Understood. I'll hold position."),
    ("ack.stopfollow.2", "I'll wait right here."),
    ("ack.stopfollow.3", "Got it, staying put."),
    ("ack.guardhere.1", "I've got this spot covered. Stay safe."),
    ("ack.guardhere.2", "Consider it done. I'll keep an eye out."),
    ("ack.guardhere.3", "I'll watch this area."),
    ("ack.guardreturn.1", "I'll patrol and come back to you."),
    ("ack.guardreturn.2", "I'll guard and return to you."),
    ("ack.guardreturn.3", "On it."),
    ("ack.wander.1", "I'll scout the area."),
    ("ack.wander.2", "On it, looking around."),
    ("ack.wander.3", "Recon mode."),
    ("ack.loot.1", "I'll scavenge what I can find."),
    ("ack.loot.2", "I'll look for supplies."),
    ("ack.loot.3", "On it."),
    ("ack.loothere.1", "I'll clear this place out."),
    ("ack.loothere.2", "I'll search this area."),
    ("ack.loothere.3", "On it, I'll loot here."),
    ("ack.harvesthere.1", "I'll chop some wood."),
    ("ack.harvesthere.2", "On it, cutting trees."),
    ("ack.harvesthere.3", "I'll gather wood here."),
    ("ack.minehere.1", "I'll mine some stone."),
    ("ack.minehere.2", "On it, breaking rocks."),
    ("ack.minehere.3", "I'll gather stone here."),
    ("ack.minehere.iron.1", "I'll mine some iron."),
    ("ack.minehere.iron.2", "On it, getting iron."),
    ("ack.minehere.coal.1", "I'll mine some coal."),
    ("ack.minehere.coal.2", "On it, getting coal."),
    ("ack.minehere.lead.1", "I'll mine some lead."),
    ("ack.minehere.lead.2", "On it, getting lead."),
    ("ack.minehere.shale.1", "I'll mine some shale."),
    ("ack.minehere.shale.2", "On it, getting shale."),
    ("ack.minehere.nitrate.1", "I'll mine some nitrate."),
    ("ack.minehere.nitrate.2", "On it, getting nitrate."),
    ("ack.digclayhere.1", "I'll dig some clay."),
    ("ack.digclayhere.2", "On it, digging for clay."),
    ("ack.digclayhere.3", "I'll gather clay here."),
    ("ack.pickhere.plants.1", "I'll pick some plants."),
    ("ack.pickhere.plants.2", "On it, gathering plants."),
    ("ack.pickhere.crops.1", "I'll harvest the crops."),
    ("ack.pickhere.crops.2", "On it, harvesting."),
    ("ack.butcherhere.1", "I'll butcher what's around here."),
    ("ack.butcherhere.2", "On it, skinning."),
    ("ack.butcherhere.3", "I'll process the carcasses here."),
    ("ack.repairhere.1", "I'll keep the base repaired."),
    ("ack.repairhere.2", "On it."),
    ("ack.repairhere.3", "Repairing the base."),
    ("ack.baserepairhere.1", "Base repair. Worst damage first."),
    ("ack.baserepairhere.2", "On it."),
    ("ack.baserepairhere.3", "Most damaged first."),
    ("ack.combatrepairhere.1", "Combat repair. I'll patch what they hit."),
    ("ack.combatrepairhere.2", "On it."),
    ("ack.combatrepairhere.3", "Under fire, I'll keep it up."),
    ("ack.rememberbase.1", "I'll remember the base."),
    ("ack.rememberbase.2", "On it, I'll take a snapshot."),
    ("ack.rememberbase.3", "Saving the base layout."),
    ("ack.medicon.1", "I'll look after everyone."),
    ("ack.medicon.2", "On it, I'll tend the whole party."),
    ("ack.medicon.3", "Medic duty, understood."),
    ("ack.medicoff.1", "Back to just looking after you."),
    ("ack.medicoff.2", "Understood, I'll stop."),
    ("ack.medicoff.3", "Medic duty off."),
    ("ack.vehicle.in.1", "Getting in."),
    ("ack.vehicle.in.2", "Coming."),
    ("ack.vehicle.out.1", "Getting out."),
    ("ack.vehicle.out.2", "Okay."),
    ("ack.minehere.rocks.1", "I'll break some rocks."),
    ("ack.minehere.rocks.2", "On it, gathering stone."),
    ("ack.jumpdown.1", "Jumping."),
    ("ack.jumpdown.2", "On it, finding an edge."),
    ("ack.setpatrolpoint.1", "I'll follow you and remember the route."),
    ("ack.setpatrolpoint.2", "Lead the way, I'm recording."),
    ("ack.setpatrolpoint.3", "On it, walking the path."),
    ("ack.patrol.1", "I'll patrol this route."),
    ("ack.patrol.2", "On it, starting the patrol."),
    ("ack.patrol.3", "Route locked in."),
    ("ack.cancelpatrolrecord.1", "Cancelled, clearing the path."),
    ("ack.cancelpatrolrecord.2", "Never mind, I'll stay here."),
    ("ack.cancelpatrolrecord.3", "Path recording aborted."),
    ("ack.setfollowdistance.1.1", "I'll stay close."),
    ("ack.setfollowdistance.1.2", "Right behind you."),
    ("ack.setfollowdistance.1.3", "Got it."),
    ("ack.setfollowdistance.5.1", "I'll give you room."),
    ("ack.setfollowdistance.5.2", "Backing up."),
    ("ack.setfollowdistance.5.3", "Got it."),
    ("ack.setfollowdistance.2_5.1", "Back to normal."),
    ("ack.setfollowdistance.2_5.2", "Got it."),
    ("ack.movetoposition.1", "On my way."),
    ("ack.movetoposition.2", "Moving there."),
    ("ack.setcrouch.1.1", "Down."),
    ("ack.setcrouch.1.2", "Taking cover."),
    ("ack.setcrouch.1.3", "Got it."),
    ("ack.setcrouch.2.1", "Up."),
    ("ack.setcrouch.2.2", "Rising."),
    ("ack.setcrouch.2.3", "Yes."),
    ("ack.setcrouch.0.1", "Copy."),
    ("ack.setcrouch.0.2", "On it."),
    ("ack.setcrouch.0.3", "As you say."),
    ("ack.settacticalmode.1.1", "Tactical mode on."),
    ("ack.settacticalmode.1.2", "Locked in."),
    ("ack.settacticalmode.1.3", "Command only."),
    ("ack.settacticalmode.0.1", "At ease."),
    ("ack.settacticalmode.0.2", "Normal mode."),
    ("ack.settacticalmode.0.3", "Relaxing."),
    ("ack.dismiss.1", "Good luck out there. Stay safe."),
    ("ack.dismiss.2", "Understood. I'll go my own way."),
    ("ack.dismiss.3", "Take care of yourself."),
    ("ack.pickup.1", "Alright, I'm on."),
    ("ack.pickup.2", "Got on. Let's move."),
    ("ack.hire.1", "Let's talk about it."),
    ("ack.hire.2", "Sure, let's see what we can work with."),
    ("ack.hire.3", "Alright, let's discuss terms."),
    ("ack.giveitem.foodCornMeal.1", "Here, eat up. You look like you could use it."),
    ("ack.giveitem.foodCornMeal.2", "Take this, you need it more than me."),
    ("ack.giveitem.drinkJarBoiledWater.1", "Thirsty? Take this \u2014 it's all I've got!"),
    ("ack.giveitem.drinkJarBoiledWater.2", "Here, drink up."),
    ("ack.giveitem.meleeToolRepairT0StoneAxe.1", "Take this. You'll need it out there."),
    ("ack.giveitem.meleeToolRepairT0StoneAxe.2", "Here, this axe is sturdy."),
    ("ack.giveitem.ammo9mmBulletBall.1", "Here. Keep it close \u2014 the dead don't stop coming."),
    ("ack.giveitem.ammo9mmBulletBall.2", "Take it, you'll need it."),
    ("ack.giveitem.gunHandgunT0PipePistol.1", "Be careful with it. Ammo's scarce."),
    ("ack.giveitem.gunHandgunT0PipePistol.2", "Here, stay sharp."),
    ("ack.giveitem.medicalFirstAidBandage.1", "Here, apply these. You'll be back on your feet soon."),
    ("ack.giveitem.medicalFirstAidBandage.2", "Take this, it'll help."),
    ("ack.opentraderinventory.1", "Take a look at my wares."),
    ("ack.opentraderinventory.2", "Here, have a look at what I've got."),
    ("ack.opentraderinventory.3", "Go ahead, take a look."),
    ("ack.opentraderinventory.4", "Let me show you what I've been carrying."),
    ("ack.opentraderinventory.5", "Sure, let's see what we can work with."),
    ("ack.opentraderinventory.6", "Of course, take your time."),
    ("ack.openinventory.1", "Take a look. Let me know if you need anything."),
    ("ack.openinventory.2", "Here, have a look at what I've got."),
    ("ack.openinventory.3", "Go ahead, take a look."),
    ("ack.openinventory.4", "Let me show you what I've been carrying."),
    ("ack.openinventory.5", "Sure, let's see what we can work with."),
    ("ack.openinventory.6", "Of course, take your time."),
    ("ack.setengage.1.1", "Weapons free."),
    ("ack.setengage.1.2", "Engaging."),
    ("ack.setengage.1.3", "On it."),
    ("ack.setengage.0.1", "Covering you."),
    ("ack.setengage.0.2", "Defensive only."),
    ("ack.setengage.0.3", "Understood."),
    ("ack.setengage.2.1", "Holding fire."),
    ("ack.setengage.2.2", "Your call."),
    ("ack.setengage.2.3", "Standing down."),
    ("ack.setengage.3.1", "Breaking off."),
    ("ack.setengage.3.2", "Falling back."),
    ("ack.setengage.3.3", "Coming back."),
    # one generic family for every weapon swap (Voice Control names the weapon: 100 lines verbatim)
    ("ack.swapweapon.1", "Switching."),
    ("ack.swapweapon.2", "Swapping weapons."),
    ("ack.swapweapon.3", "Got it, switching."),
    ("ack.swapweapon.4", "One sec."),
    ("ack.swapweapon.5", "Changing up."),
    ("ack.swapweapon.6", "On it."),
    # one generic family for every formation (Voice Control names the side: 38 lines verbatim)
    ("ack.setformation.1", "In position."),
    ("ack.setformation.2", "Taking my spot."),
    ("ack.setformation.3", "Got my side."),
    ("ack.setformation.4", "Moving into position."),
    ("ack.setformation.5", "Holding my sector."),
    ("ack.setformation.6", "Formation, got it."),
]

# Voice Control's hard-coded spoken lines (1-XNPCVoiceControl.dll 3.4.01), its billing lines left out.
VC = [
    ("vc.leaderless.1", "I don't take orders from just anyone. Hire me first."),
    ("vc.leaderless.2", "We're not on those terms yet. Make me an offer."),
    ("vc.leaderless.3", "I work for myself until someone proves they're worth following."),
    ("vc.leaderless.4", "You'll need to earn my trust before I follow your commands."),
    ("vc.leaderless.5", "Not interested. Convince me to join you and we'll talk."),
    ("vc.leaderless.6", "I'm not hired yet, what have you got to offer?"),
    ("vc.nothingtoshow", "I've got nothing to show you right now."),
    ("vc.swap.cant", "Sorry, I can't switch weapons right now."),
    ("vc.swap.which", "Which weapon do you want me to use?"),
    ("vc.swap.error", "Something went wrong switching weapons."),
    ("vc.swap.notcarried", "I don't have that on me."),
    ("vc.swap.noneofcat", "I'm not carrying one."),
    ("vc.swap.unknown", "I don't know which weapon that is."),
    ("vc.swap.nothing", "I'm not carrying anything."),
    ("vc.confirm.ask", "Are you sure? Say yes to confirm or no to cancel."),
    ("vc.confirm.repeat", "Say yes to confirm, or no to cancel."),
    ("vc.confirm.cancel", "Understood."),
    ("vc.patrol.show", "Follow me and I'll show you the route."),
    ("vc.patrol.go", "I'll patrol this route."),
    ("vc.patrol.cancel", "Cancelled, clearing the path."),
    ("vc.gothere.floor", "Heading over there."),
    ("vc.gothere.go", "On my way."),
    ("vc.gothere.inway", "I'm in your way, point past me."),
    ("vc.gothere.far", "That's too far."),
    ("vc.radial.unknown", "I don't know how to do that."),
    ("vc.tactical.1", "Say again?"),
    ("vc.tactical.2", "Didn't catch that."),
    ("vc.tactical.3", "Repeat the order."),
    ("vc.tactical.4", "Come again?"),
    ("vc.fallback.placeholder", "Sorry, could you say that again?"),
    ("vc.fallback.llmerror", "Hmm? My mind wandered for a second there."),
    ("vc.fallback.story", "I've seen a lot of things out here... but I'd rather not talk about it."),
    ("vc.action.follow", "On it. I'll follow you."),
    ("vc.action.stay", "Understood. I'll stay here."),
    ("vc.action.guard", "I've got this spot covered."),
    ("vc.action.trade", "Take a look at what I've got."),
    ("vc.action.give", "Here, take this."),
    ("vc.action.heal", "Let me patch you up."),
    ("vc.action.refuse", "I can't do that."),
    ("vc.bag.empty", "I am not carrying any supplies."),
]

NEW = [("greet", GREET), ("chatter", CHATTER), ("taunt", TAUNT), ("xn", XN), ("ack", ACK), ("vc", VC)]
FAMILIES = ["dialog"] + [f for f, _ in NEW]


# ------------------------------------------------------------------------------------------------------- the lines ----

def localization():
    """SC's Localization.csv: key -> (file, type, English text), in file order."""
    out = {}
    with io.open(os.path.join(MOD, "Config", "Localization.csv"), "r", encoding="utf-8", newline="") as f:
        rows = csv.reader(f)
        header = next(rows)
        col, fcol, tcol = header.index("english"), header.index("File"), header.index("Type")
        for r in rows:
            if r and len(r) > col:
                out[r[0]] = (r[fcol], r[tcol], r[col])
    return out


def english():
    """SC's Localization.csv: key -> English text, in file order."""
    return {k: v[2] for k, v in localization().items()}


def lines(problems):
    """key -> the words said, for every dialog line: XNPCCore's, the dialog file's statements in order, then the runtime
    lines. What can't be voiced goes to problems."""
    loc = localization()
    words = {k: v[2] for k, v in loc.items()}
    with io.open(os.path.join(MOD, "Config", "dialogs.xml"), "r", encoding="utf-8") as f:
        xml = re.sub(r"<!--.*?-->", "", f.read(), flags=re.S)
    out = dict(EXTRA)

    def add(key, said):
        if "{" in said:
            problems.append("%s has a token and no spoken part (SPOKEN): %s" % (key, said))
        elif key in out:
            problems.append("%s is two lines" % key)
        else:
            out[key] = said

    for m in re.finditer(r"<statement\b([^>]*)>", xml):
        attrs = dict(re.findall(r'(\w+)\s*=\s*"([^"]*)"', m.group(1)))
        sid, key = attrs.get("id"), attrs.get("text")
        if sid and key and sid not in RUNTIME_STATEMENTS:
            add(sid, SPOKEN.get(sid, words.get(key, key)))
    for stem in RUNTIME_FAMILIES:
        for k in sorted(words):
            if (k == stem or k.startswith(stem + "_")) and loc[k][:2] == ("Dialog", "Statement"):
                add(k, words[k])
    for k in RUNTIME_FIXED:
        if k in words:
            add(k, words[k])
        else:
            problems.append("no Localization row for " + k)
    return out


def new_lines(problems):
    """key -> (family, words) for every V2 line, in family order. A key used twice, or with a token, goes to problems."""
    out = {}
    for fam, rows in NEW:
        for k, t in rows:
            if k in out:
                problems.append("%s is two lines" % k)
            elif "{" in t:
                problems.append("%s has a token: %s" % (k, t))
            elif not k.startswith(fam + "."):
                problems.append("%s is not in its family %s" % (k, fam))
            else:
                out[k] = (fam, t)
    return out


def every_line(problems):
    """key -> (family, words): the dialog lines, then the V2 lines."""
    out = {k: ("dialog", v) for k, v in lines(problems).items()}
    for k, v in new_lines(problems).items():
        if k in out:
            problems.append("%s is both a dialog line and a V2 line" % k)
        else:
            out[k] = v
    return out


# ------------------------------------------------------------------------------------------------------ the voices ----

def voice_table():
    """Config/blocks.xml's Voices list (comments stripped), else DEFAULT_VOICES."""
    try:
        with io.open(os.path.join(MOD, "Config", "blocks.xml"), "r", encoding="utf-8") as f:
            xml = re.sub(r"<!--.*?-->", "", f.read(), flags=re.S)
        m = re.search(r'<property\s+name="Voices"\s+value="([^"]*)"', xml)
        if m:
            return [v.strip() for v in m.group(1).split(",") if v.strip()]
    except (IOError, OSError):
        pass
    return list(DEFAULT_VOICES)


def disk_voices():
    if not os.path.isdir(VOICES):
        return []
    return sorted(d for d in os.listdir(VOICES) if os.path.isdir(os.path.join(VOICES, d)))


def all_voices():
    """The table's voices in its order, then any other folder on disk."""
    table = voice_table()
    seen = {v.lower() for v in table}
    return table + [d for d in disk_voices() if d.lower() not in seen]


def name_ok(d):
    """<male|female>_<Race>_<Name>, Race one of RACES (an old <sex>_<Name> folder still plays, raceless)."""
    m = re.match(r"(?i)^(male|female)_([^_]+)(?:_(.+))?$", d)
    if not m:
        return False
    return m.group(3) is None or m.group(2) in RACES


def clips(d):
    path = os.path.join(VOICES, d)
    if not os.path.isdir(path):
        return None
    return {os.path.splitext(f)[0] for f in os.listdir(path) if f.lower().endswith(".ogg")}


# ---------------------------------------------------------------------------------------------------- ScVoiceLines ----

def cs_string(s):
    out = []
    for ch in s:
        if ch == "\\":
            out.append("\\\\")
        elif ch == '"':
            out.append('\\"')
        elif ord(ch) < 32 or ord(ch) > 126:
            out.append("\\u%04x" % ord(ch))
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


def cs_table():
    """ScVoiceLines.cs as gencs writes it."""
    o = []
    o.append("// <auto-generated>")
    o.append("// Written by Tools/voice_lines.py gencs from its V2 families. Edit the lines there and run it again;")
    o.append("// \"python -I Tools/voice_lines.py check\" says when this file is stale.")
    o.append("// </auto-generated>")
    o.append("namespace SurvivorCommunity")
    o.append("{")
    o.append("    /// <summary>")
    o.append("    /// V2: SC's own voiced lines outside a talk (greetings, chatter, raider taunts) and the XNPCCore / Voice Control lines")
    o.append("    /// SC voices, as key, words, key, words, ... in family order. A clip is &lt;key&gt;.ogg in a voice's folder; the words")
    o.append("    /// are the caption, and for the xn, ack and vc families Voice Control's own English, which ScBarks matches.")
    o.append("    /// </summary>")
    o.append("    public static class ScVoiceLines")
    o.append("    {")
    o.append("        /// <summary>The families, in recording order.</summary>")
    o.append("        public static readonly string[] Families = { " + ", ".join(cs_string(f) for f, _ in NEW) + " };")
    o.append("")
    o.append("        public static readonly string[] Lines =")
    o.append("        {")
    for fam, rows in NEW:
        o.append("            // %s (%d)" % (fam, len(rows)))
        for k, t in rows:
            o.append("            %s, %s," % (cs_string(k), cs_string(t)))
    o.append("        };")
    o.append("    }")
    o.append("}")
    return "\n".join(o) + "\n"


def cs_state():
    """'in step', 'missing' or 'stale'."""
    if not os.path.isfile(CS_TABLE):
        return "missing"
    with io.open(CS_TABLE, "r", encoding="utf-8", newline="") as f:
        return "in step" if f.read() == cs_table() else "stale"


# ------------------------------------------------------------------------------------------------------- commands ----

def flag(args, name):
    if name in args:
        args.remove(name)
        return True
    return False


def option(args, name):
    if name in args:
        i = args.index(name)
        if i + 1 < len(args):
            v = args[i + 1]
            del args[i:i + 2]
            return v
        del args[i]
    return None


def wrap(prefix, items, width=118):
    out, cur = [], prefix
    for i, it in enumerate(items):
        piece = it + (", " if i < len(items) - 1 else "")
        if len(cur) + len(piece) > width and cur.strip():
            out.append(cur.rstrip())
            cur = " " * len(prefix)
        cur += piece
    out.append(cur.rstrip())
    return out


def pick(every, args):
    """The lines --new / --family / --voice select, in order."""
    new_only = flag(args, "--new")
    fams = option(args, "--family")
    voice = option(args, "--voice")
    fams = [f.strip() for f in fams.split(",")] if fams else None
    have = None
    if voice is not None:
        have = clips(voice) or set()
    out = []
    for k, (fam, t) in every.items():
        if new_only and fam == "dialog":
            continue
        if fams and fam not in fams:
            continue
        if have is not None and k in have:
            continue
        out.append((k, fam, t))
    return out, fams


def cmd_list(every, problems, args):
    tsv = flag(args, "--tsv")
    rows, fams = pick(every, args)
    if fams:
        for f in fams:
            if f not in FAMILIES:
                problems.append("no family " + f + " (" + ", ".join(FAMILIES) + ")")
    for k, fam, t in rows:
        print(("%s\t%s" % (k, t)) if tsv else ("%-34s %s" % (k, t)))
    if not tsv:
        print("%d lines, %d characters" % (len(rows), sum(len(t) for _, _, t in rows)))
    for p in problems:
        print("PROBLEM " + p)


def cmd_check(every, problems, args):
    keys = flag(args, "--keys")
    bad = len(problems)
    for p in problems:
        print("PROBLEM " + p)
    state = cs_state()
    new_count = sum(len(r) for _, r in NEW)
    print("ScVoiceLines.cs: %s (%d V2 lines)%s" % (state, new_count, "" if state == "in step" else " - run gencs"))
    if state != "in step":
        bad += 1
    table = voice_table()
    print("voice table (Config/blocks.xml Voices): %d voices" % len(table))
    for r in RACES:
        for sex in ("male", "female"):
            n = sum(1 for v in table if v.lower().startswith(sex + "_" + r.lower() + "_"))
            if n != 2:
                print("  %s %s: %d voices in the table (the plan is two)" % (r, sex, n))
    by_fam = {}
    for k, (fam, _) in every.items():
        by_fam.setdefault(fam, []).append(k)
    total = len(every)
    for d in all_voices():
        listed = "" if d in table else " (not in the table)"
        if not name_ok(d):
            print("%s%s: not named <male|female>_<Race>_<Name> with Race one of %s - the game skips it" % (d, listed, "/".join(RACES)))
            bad += 1
            continue
        have = clips(d)
        if have is None:
            print("%s%s: no folder yet - all %d lines to record" % (d, listed, total))
            if keys:
                for fam in FAMILIES:
                    for line in wrap("  %s: " % fam, by_fam.get(fam, [])):
                        print(line)
            continue
        missing = [k for k in every if k not in have]
        unknown = sorted(k for k in have if k not in every)
        parts = []
        for fam in FAMILIES:
            ks = by_fam.get(fam, [])
            parts.append("%s %d/%d" % (fam, sum(1 for k in ks if k in have), len(ks)))
        print("%s%s: %d of %d lines (%s)" % (d, listed, total - len(missing), total, ", ".join(parts)))
        for fam in FAMILIES:
            ks = [k for k in by_fam.get(fam, []) if k not in have]
            if ks:
                for line in wrap("  no clip yet, %s: " % fam, ks):
                    print(line)
        if unknown:
            for line in wrap("  no such line: ", unknown):
                print(line)
            bad += len(unknown)
    sys.exit(1 if bad else 0)


def cmd_cost(every, problems):
    for p in problems:
        print("PROBLEM " + p)
    print("Characters per voice (about one ElevenLabs credit each):")
    print("  %-10s %6s %7s   %s" % ("family", "lines", "chars", "unique words (lines/chars)"))
    tot_lines = tot_chars = 0
    rows = []
    for fam in FAMILIES:
        ts = [t for k, (f, t) in every.items() if f == fam]
        uniq = sorted(set(ts))
        rows.append((fam, len(ts), sum(len(t) for t in ts), len(uniq), sum(len(t) for t in uniq)))
    for fam, n, c, un, uc in rows:
        print("  %-10s %6d %7d   %d/%d" % (fam, n, c, un, uc))
    new_n = sum(r[1] for r in rows if r[0] != "dialog")
    new_c = sum(r[2] for r in rows if r[0] != "dialog")
    dia = [r for r in rows if r[0] == "dialog"][0]
    print("  %-10s %6d %7d" % ("new (V2)", new_n, new_c))
    print("  %-10s %6d %7d" % ("full set", new_n + dia[1], new_c + dia[2]))
    print("Still to record, per voice (from the folders):")
    all_c = 0
    for d in all_voices():
        have = clips(d) or set()
        miss = [(k, f, t) for k, (f, t) in every.items() if k not in have]
        mn = sum(1 for _, f, _ in miss if f != "dialog")
        mc = sum(len(t) for _, f, t in miss if f != "dialog")
        dn = sum(1 for _, f, _ in miss if f == "dialog")
        dc = sum(len(t) for _, f, t in miss if f == "dialog")
        all_c += mc + dc
        print("  %-26s new %3d lines %6d chars, dialog %3d lines %6d chars, total %6d%s"
              % (d, mn, mc, dn, dc, mc + dc, "" if clips(d) is not None else "  (no folder yet)"))
    print("All voices together: %d characters" % all_c)


def cmd_sidecars(every, problems, args):
    voice = option(args, "--voice")
    rows, _ = pick(every, args)          # --new / --family only: every key, clip or not
    targets = [voice] if voice else [d for d in disk_voices()]
    for d in targets:
        if not name_ok(d):
            print("%s: not a voice folder name - skipped" % d)
            continue
        path = os.path.join(VOICES, d)
        if not os.path.isdir(path):
            os.makedirs(path)
        for k, _, t in rows:
            with io.open(os.path.join(path, k + ".txt"), "w", encoding="utf-8", newline="\n") as f:
                f.write(t + "\n")
        print("%s: %d sidecars" % (d, len(rows)))
    for p in problems:
        print("PROBLEM " + p)


def main():
    args = sys.argv[1:]
    cmd = args.pop(0) if args else "list"
    problems = []
    every = every_line(problems)
    if cmd == "list":
        cmd_list(every, problems, args)
    elif cmd == "check":
        cmd_check(every, problems, args)
    elif cmd == "cost":
        cmd_cost(every, problems)
    elif cmd == "sidecars":
        cmd_sidecars(every, problems, args)
    elif cmd == "gencs":
        with io.open(CS_TABLE, "w", encoding="utf-8", newline="\n") as f:
            f.write(cs_table())
        print("ScVoiceLines.cs: %d V2 lines in %d families" % (sum(len(r) for _, r in NEW), len(NEW)))
        for p in problems:
            print("PROBLEM " + p)
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    main()
