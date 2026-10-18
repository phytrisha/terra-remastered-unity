using UnityEngine;

/// Points of interest on the white tiger for the showcase: where they sit on the rig, where the camera looks from,
/// and the text the HUD shows. Positions are in the tiger's root space at rest (metres, +Z = snout direction) and are
/// converted to bone-local offsets at runtime, so they follow the idle animation.
public static class TigerFacts
{
    public struct Stat { public string label, value; public float bar; public Stat(string l, string v, float b = -1) { label = l; value = v; bar = b; } }

    public class Point
    {
        public string id, code, title, subtitle, bone, body;
        public Vector3 restPos;          // root space, rest pose
        public Vector3 viewDir;          // root space, from the point toward the camera
        public float viewDist;
        public Stat[] stats;
    }

    public const string Specimen = "PANTHERA TIGRIS TIGRIS";
    public const string Variant = "BENGAL TIGER  //  WHITE MORPH";

    public static readonly Point[] Points =
    {
        new Point {
            id = "eyes", code = "01", title = "EYES", subtitle = "Ocular system", bone = "head",
            restPos = new Vector3(-0.03f, 0.93f, 0.94f), viewDir = new Vector3(0.35f, 0.12f, 1f), viewDist = 0.85f,
            stats = new[] { new Stat("Iris colour", "Amber (blue is commoner)"), new Stat("Pupil", "Round"), new Stat("Night vision", "~6x human", 0.86f), new Stat("Binocular overlap", "Wide", 0.7f) },
            body = "Most white tigers have blue eyes: the same gene variant that removes the orange from the coat usually lightens the iris too. This one kept the amber eyes of an orange tiger. "
                 + "A reflective layer behind the retina, the tapetum lucidum, sends light through the eye a second time, which is what makes a tiger's eyes glow at night and lets it hunt at dusk and in the dark."
        },
        new Point {
            id = "teeth", code = "02", title = "TEETH", subtitle = "Dentition & bite", bone = "head",
            restPos = new Vector3(-0.04f, 0.75f, 1.0f), viewDir = new Vector3(0.25f, -0.05f, 1f), viewDist = 0.75f,
            stats = new[] { new Stat("Teeth", "30"), new Stat("Canine length", "up to ~7.5 cm", 0.9f), new Stat("Bite force", "~1,000 psi", 0.8f) },
            body = "Tigers have the longest canines of any living cat. They grip and hold, while the blade-like carnassial teeth further back slice meat like scissors. "
                 + "The tongue is covered in hard, backward-pointing papillae that rasp meat from bone."
        },
        new Point {
            id = "whiskers", code = "03", title = "WHISKERS", subtitle = "Vibrissae", bone = "head",
            restPos = new Vector3(0.09f, 0.79f, 0.96f), viewDir = new Vector3(1f, 0.1f, 0.55f), viewDist = 0.7f,
            stats = new[] { new Stat("Type", "Tactile hairs"), new Stat("Sensitivity", "Air currents", 0.75f), new Stat("Use", "Dark, close range") },
            body = "Each whisker sits in a follicle packed with nerves. They pick up the slightest air movement and touch, helping the tiger move through dense forest at night "
                 + "and sense the exact position of prey when it is too close to see clearly."
        },
        new Point {
            id = "ears", code = "04", title = "EARS", subtitle = "Hearing & signals", bone = "ear_R",
            restPos = new Vector3(0.1f, 1.03f, 0.86f), viewDir = new Vector3(0.55f, 0.65f, 0.55f), viewDist = 0.8f,
            stats = new[] { new Stat("Hearing range", "Beyond human", 0.8f), new Stat("Ear rotation", "Independent"), new Stat("Ear spots", "Ocelli") },
            body = "Tigers hear sounds well above and below the human range and can turn each ear on its own toward a noise. "
                 + "The backs of the ears carry pale spots (ocelli), thought to work as signals between tigers, for instance for cubs following their mother."
        },
        new Point {
            id = "coat", code = "05", title = "COAT", subtitle = "Fur, pigment & stripes", bone = "spine2",
            restPos = new Vector3(0.27f, 0.72f, -0.08f), viewDir = new Vector3(1f, 0.3f, 0.15f), viewDist = 1.6f,
            stats = new[] { new Stat("Gene", "SLC45A2 variant"), new Stat("Inheritance", "Recessive (both parents)"), new Stat("Stripes", "Unique per tiger", 1f), new Stat("Wild sightings", "None since 1958", 0.05f) },
            body = "A white tiger is not an albino. A recessive change in the SLC45A2 gene stops the red-yellow pigment, while the black pigment stays, so the stripes remain dark on white fur. "
                 + "The stripes continue on the skin underneath and are as individual as fingerprints. Today's white tigers live in captivity; many descend from Mohan, a white male caught in Rewa, India, in 1951."
        },
        new Point {
            id = "paws", code = "06", title = "PAWS", subtitle = "Claws & pads", bone = "toe_FR",
            restPos = new Vector3(0.18f, 0.07f, 0.66f), viewDir = new Vector3(0.55f, 0.25f, 1f), viewDist = 0.9f,
            stats = new[] { new Stat("Claws", "Retractable"), new Stat("Claw length", "up to ~10 cm", 0.85f), new Stat("Leap", "up to ~10 m", 0.9f) },
            body = "Thick soft pads let a tiger stalk almost silently. The claws stay sheathed while walking, which keeps them sharp, and extend for gripping prey or climbing. "
                 + "Powerful forelegs carry most of the weight in a pounce and can bring down animals several times the tiger's own weight."
        },
        new Point {
            id = "tail", code = "07", title = "TAIL", subtitle = "Balance & mood", bone = "tail4",
            restPos = new Vector3(0.06f, 0.24f, -0.84f), viewDir = new Vector3(0.8f, 0.35f, -0.6f), viewDist = 1.3f,
            stats = new[] { new Stat("Length", "~0.9-1.1 m", 0.7f), new Stat("Rings", "Dark, black tip"), new Stat("Function", "Counterbalance") },
            body = "The long tail acts as a counterweight in sharp turns and in the air during a leap. Its position and movement also show mood: "
                 + "held low and relaxed when calm, twitching or lashing when the tiger is tense."
        },
    };
}
