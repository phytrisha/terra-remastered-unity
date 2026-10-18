using UnityEngine;
using Point = TigerFacts.Point;
using Stat = TigerFacts.Stat;

/// Showcase data per species (header lines, forest reticle label, hotspots). TigerEncounter looks it up by its
/// speciesId. Hotspot positions are in the animal's root space at rest (metres, +Z = snout), like TigerFacts; for the
/// tapir / chital they come from the HD meshes (Blender (x, y, z) -> root (-x, z, -y)). Facts are kept to
/// well-established points; numbers only where they are textbook values.
public static class AnimalFacts
{
    public class Species
    {
        public string id, title, latin, variant, archive, reticle;
        public Point[] points;
    }

    public static Species Get(string id) => id switch { "tapir" => Tapir, "chital" => Chital, _ => Tiger };

    public static readonly Species Tiger = new()
    {
        id = "tiger", title = "WHITE TIGER", latin = TigerFacts.Specimen, variant = TigerFacts.Variant,
        archive = "ARCHIVE 07  //  SPECIMEN 01", reticle = "WHITE TIGER  //  CLICK TO APPROACH", points = TigerFacts.Points
    };

    public static readonly Species Tapir = new()
    {
        id = "tapir", title = "MALAYAN TAPIR", latin = "TAPIRUS INDICUS", variant = "ODD-TOED UNGULATE  //  ADULT",
        archive = "ARCHIVE 07  //  SPECIMEN 02", reticle = "MALAYAN TAPIR  //  CLICK TO APPROACH",
        points = new[]
        {
            new Point {
                id = "trunk", code = "01", title = "PROBOSCIS", subtitle = "Nose & upper lip", bone = "snout",
                restPos = new Vector3(0f, 0.64f, 0.93f), viewDir = new Vector3(-0.8f, 0.1f, 0.6f), viewDist = 0.9f,
                stats = new[] { new Stat("Formed from", "Nose + upper lip"), new Stat("Grip", "Prehensile", 0.75f), new Stat("Sense of smell", "Excellent", 0.92f) },
                body = "The short trunk is the tapir's nose and upper lip grown into one flexible organ. It curls around shoots and leaves to pull them into the mouth and keeps sniffing the air. "
                     + "Tapirs are strong swimmers and can hold the trunk above the surface like a snorkel."
            },
            new Point {
                id = "eyes", code = "02", title = "SENSES", subtitle = "Eyes, nose & ears", bone = "head",
                restPos = new Vector3(-0.10f, 0.87f, 0.73f), viewDir = new Vector3(-1f, 0.15f, 0.6f), viewDist = 0.8f,
                stats = new[] { new Stat("Eyesight", "Poor", 0.25f), new Stat("Smell", "Excellent", 0.92f), new Stat("Hearing", "Keen", 0.8f), new Stat("Active", "Dusk & night") },
                body = "Tapirs have small eyes and weak eyesight. They find their way through the forest by smell and hearing, mostly at dusk and at night, "
                     + "when a good nose locates food and danger long before eyes could."
            },
            new Point {
                id = "ears", code = "03", title = "EARS", subtitle = "Hearing & calls", bone = "ear_L",
                restPos = new Vector3(-0.14f, 1.05f, 0.55f), viewDir = new Vector3(-0.6f, 0.4f, 0.7f), viewDist = 0.9f,
                stats = new[] { new Stat("Shape", "Rounded, white rim"), new Stat("Calls", "Whistles & squeals") },
                body = "The rounded ears turn to follow sounds and are edged with white. Tapirs are mostly solitary; they keep in touch with high-pitched whistles, "
                     + "and a mother and her calf call to each other the same way."
            },
            new Point {
                id = "saddle", code = "04", title = "SADDLE", subtitle = "Coat pattern", bone = "spine1",
                restPos = new Vector3(-0.28f, 0.85f, -0.35f), viewDir = new Vector3(-1f, 0.25f, 0.1f), viewDist = 1.7f,
                stats = new[] { new Stat("Adult coat", "Black / white saddle"), new Stat("Calves", "Brown, striped + spotted") },
                body = "The white saddle breaks up the tapir's outline: on the moonlit forest floor a resting tapir reads as a pale rock or a gap in the shadows rather than an animal. "
                     + "Calves are born brown with white stripes and spots, camouflage for dappled light, and grow into the adult pattern within their first year."
            },
            new Point {
                id = "feet", code = "05", title = "FEET", subtitle = "Toes & gait", bone = "foot_FL",
                restPos = new Vector3(-0.12f, 0.06f, 0.04f), viewDir = new Vector3(-0.7f, 0.35f, 0.6f), viewDist = 0.75f,
                stats = new[] { new Stat("Front feet", "4 toes"), new Stat("Hind feet", "3 toes"), new Stat("Order", "Perissodactyla") },
                body = "Tapirs are odd-toed ungulates, related to horses and rhinoceroses. Each front foot has four toes and each hind foot three; "
                     + "the splayed toes spread the animal's weight on soft, muddy ground."
            },
            new Point {
                id = "status", code = "06", title = "STATUS", subtitle = "Range & conservation", bone = "tail1",
                restPos = new Vector3(0f, 0.75f, -0.98f), viewDir = new Vector3(-0.5f, 0.3f, -1f), viewDist = 1.0f,
                stats = new[] { new Stat("IUCN", "Endangered", 0.8f), new Stat("Range", "Malay Peninsula, Sumatra"), new Stat("Pregnancy", "~13 months") },
                body = "The Malayan tapir is the largest of the tapirs and the only one native to Asia. It is listed as Endangered: logging, plantations and roads cut its forest into fragments. "
                     + "A female raises a single calf after a pregnancy of about thirteen months."
            },
        }
    };

    public static readonly Species Chital = new()
    {
        id = "chital", title = "CHITAL", latin = "AXIS AXIS", variant = "SPOTTED DEER  //  DOE",
        archive = "ARCHIVE 07  //  SPECIMEN 03", reticle = "CHITAL  //  CLICK TO APPROACH",
        points = new[]
        {
            new Point {
                id = "eyes", code = "01", title = "EYES", subtitle = "Vigilance", bone = "head",
                restPos = new Vector3(-0.07f, 1.21f, 0.66f), viewDir = new Vector3(-1f, 0.15f, 0.6f), viewDist = 0.75f,
                stats = new[] { new Stat("Placement", "Sides of the head"), new Stat("Field of view", "Wide", 0.85f), new Stat("Scent gland", "In front of the eye") },
                body = "Large eyes set on the sides of the head give the chital a wide view for spotting predators while it feeds. "
                     + "The dark patch in front of each eye is a scent gland that deer rub on twigs to mark their surroundings."
            },
            new Point {
                id = "ears", code = "02", title = "EARS", subtitle = "Hearing & alarm calls", bone = "ear_L",
                restPos = new Vector3(-0.15f, 1.31f, 0.53f), viewDir = new Vector3(-0.5f, 0.3f, 0.8f), viewDist = 0.9f,
                stats = new[] { new Stat("Movement", "Independent"), new Stat("Alarm", "Sharp bark") },
                body = "Big, mobile ears swivel independently to pinpoint a sound. Chital react to the alarm calls of langur monkeys and other animals, "
                     + "and give their own sharp alarm bark when they spot a tiger or a leopard."
            },
            new Point {
                id = "coat", code = "03", title = "COAT", subtitle = "Spots for life", bone = "spine1",
                restPos = new Vector3(-0.17f, 0.95f, -0.25f), viewDir = new Vector3(-1f, 0.2f, 0.1f), viewDist = 1.4f,
                stats = new[] { new Stat("Spots", "Kept for life"), new Stat("Colour", "Rufous, white spots"), new Stat("Back", "Dark stripe") },
                body = "Most deer lose their spots when they grow up; the chital keeps them all its life. White spots on a rufous coat blend into the sun-flecked light of the forest edge, "
                     + "and a dark stripe runs along the spine."
            },
            new Point {
                id = "tail", code = "04", title = "TAIL", subtitle = "Alarm signal", bone = "tail1",
                restPos = new Vector3(0f, 0.90f, -0.80f), viewDir = new Vector3(-0.4f, 0.3f, -1f), viewDist = 1.0f,
                stats = new[] { new Stat("Signal", "Tail raised, white flash"), new Stat("Lives in", "Herds") },
                body = "When alarmed, a chital lifts its tail and flashes the white underside and rump, a signal the rest of the herd can follow as it flees. "
                     + "Herds often feed below langur monkeys, picking up the fruit and leaves they drop, and both species gain from the other's watchfulness."
            },
            new Point {
                id = "legs", code = "05", title = "LEGS", subtitle = "Cloven hooves", bone = "foot_FL",
                restPos = new Vector3(-0.095f, 0.05f, 0.15f), viewDir = new Vector3(-0.7f, 0.3f, 0.6f), viewDist = 0.7f,
                stats = new[] { new Stat("Order", "Artiodactyla"), new Stat("Hoof", "Cloven, two toes"), new Stat("Escape", "Sprint & leap", 0.85f) },
                body = "Long, slender legs end in cloven hooves: chital are even-toed ungulates that walk on the tips of two toes. "
                     + "They are built for sudden bursts of speed and quick leaps through the undergrowth."
            },
            new Point {
                id = "diet", code = "06", title = "DIET", subtitle = "Food & status", bone = "head",
                restPos = new Vector3(0f, 1.10f, 0.80f), viewDir = new Vector3(-0.6f, 0.1f, 1f), viewDist = 0.75f,
                stats = new[] { new Stat("Diet", "Grass, leaves, fruit"), new Stat("IUCN", "Least Concern", 0.15f), new Stat("Predators", "Tiger, leopard, dhole") },
                body = "Chital graze on grasses and browse leaves, flowers and fallen fruit. They are the most common deer in India's forests and the main prey of the tiger there. "
                     + "Only the stags grow antlers; this doe has none."
            },
        }
    };
}
