# Review renders of the tiger. TAG env names the round; FRAME env poses the rig at that frame.
tag = os.environ.get("TAG", "review"); fr = os.environ.get("FRAME")
if fr: bpy.context.scene.frame_set(int(fr))
views = tuple(os.environ.get("VIEWS", "front,back,left,right,top,threequarter").split(","))
result = review_views("SM_WhiteTiger", tag=tag, views=views, res=(1000, 700))
