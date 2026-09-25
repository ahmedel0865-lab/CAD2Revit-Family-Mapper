# -*- coding: utf-8 -*-
"""A tiny stand-in for the Revit API, just enough to exercise placer.py's
control flow outside Revit. It is NOT a Revit simulator."""
import math
import sys
import types


class XYZ(object):
    def __init__(self, x=0.0, y=0.0, z=0.0):
        self.X, self.Y, self.Z = float(x), float(y), float(z)

    def Add(self, o):
        return XYZ(self.X + o.X, self.Y + o.Y, self.Z + o.Z)

    def Subtract(self, o):
        return XYZ(self.X - o.X, self.Y - o.Y, self.Z - o.Z)

    def Multiply(self, k):
        return XYZ(self.X * k, self.Y * k, self.Z * k)

    def Negate(self):
        return self.Multiply(-1)

    def DotProduct(self, o):
        return self.X * o.X + self.Y * o.Y + self.Z * o.Z

    def CrossProduct(self, o):
        return XYZ(self.Y * o.Z - self.Z * o.Y, self.Z * o.X - self.X * o.Z,
                   self.X * o.Y - self.Y * o.X)

    def GetLength(self):
        return math.sqrt(self.DotProduct(self))

    def Normalize(self):
        return self.Multiply(1.0 / self.GetLength())


XYZ.BasisZ = XYZ(0, 0, 1)


class ElementId(object):
    def __init__(self, v):
        self.Value = v

    def __eq__(self, o):
        return isinstance(o, ElementId) and o.Value == self.Value


class Enum(object):
    def __init__(self, *names):
        for n in names:
            setattr(self, n, n)


FamilyPlacementType = Enum("OneLevelBased", "OneLevelBasedHosted", "WorkPlaneBased", "CurveBased")
BuiltInParameter = Enum("INSTANCE_ELEVATION_PARAM", "INSTANCE_FREE_HOST_OFFSET_PARAM",
                        "INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM", "ALL_MODEL_INSTANCE_COMMENTS")
FailureProcessingResult = Enum("Continue")
FailureSeverity = Enum("Warning")
TransactionStatus = Enum("Committed", "RolledBack")
StructuralType = Enum("NonStructural")


class IFailuresPreprocessor(object):
    pass


class Param(object):
    def __init__(self):
        self.IsReadOnly = False
        self.value = None

    def Set(self, v):
        self.value = v
        return True


class Location(object):
    def __init__(self, pt):
        self.Point = pt


class Family(object):
    def __init__(self, fid, ptype):
        self.Id = ElementId(fid)
        self.FamilyPlacementType = ptype


class FamilySymbol(object):
    def __init__(self, sid, family):
        self.Id = ElementId(sid)
        self.Family = family
        self.IsActive = False

    def Activate(self):
        self.IsActive = True


class FamilyInstance(object):
    def __init__(self, doc, sym, pt, how):
        self.Id = ElementId(doc.next_id())
        self.Symbol = sym
        self.Location = Location(pt)
        self.how = how
        self.params = {}
        self.rotation = 0.0

    def get_Parameter(self, bip):
        return self.params.setdefault(bip, Param())


class Level(object):
    def __init__(self, lid, z):
        self.Id = ElementId(lid)
        self.ProjectElevation = z

    def GetPlaneReference(self):
        return ("levelplane", self)


class Creator(object):
    def __init__(self, doc):
        self.doc = doc

    def NewFamilyInstance(self, *a):
        if isinstance(a[0], XYZ) and isinstance(a[2], Level):      # level-based
            inst = FamilyInstance(self.doc, a[1], a[0], "level")
        elif isinstance(a[0], XYZ):                                 # legacy hosted
            inst = FamilyInstance(self.doc, a[1], a[0], "hosted-legacy")
        elif a[0][0] == "levelplane":                               # level work plane
            inst = FamilyInstance(self.doc, a[3], a[1], "levelplane")
        else:                                                       # face reference
            inst = FamilyInstance(self.doc, a[3], a[1], "face")
        self.doc.pending.append(inst)
        return inst


class Doc(object):
    def __init__(self):
        self.elements = []   # committed
        self.pending = []    # in open transaction
        self._id = 1000
        self.Create = Creator(self)

    def next_id(self):
        self._id += 1
        return self._id

    def Regenerate(self):
        pass

    def Delete(self, eid):
        pass


class FilteredElementCollector(object):
    def __init__(self, doc):
        self.doc = doc

    def OfClass(self, cls):
        return [e for e in self.doc.elements if isinstance(e, cls)]


class _Opts(object):
    def SetFailuresPreprocessor(self, p):
        pass

    def SetClearAfterRollback(self, v):
        pass


class Transaction(object):
    def __init__(self, doc, name):
        self.doc, self.started, self.ended = doc, False, False

    def GetFailureHandlingOptions(self):
        return _Opts()

    def SetFailureHandlingOptions(self, o):
        pass

    def Start(self):
        self.started = True
        self.doc.pending = []

    def Commit(self):
        self.doc.elements.extend(self.doc.pending)
        self.doc.pending, self.ended = [], True
        return TransactionStatus.Committed

    def RollBack(self):
        self.doc.pending, self.ended = [], True

    def HasStarted(self):
        return self.started

    def HasEnded(self):
        return self.ended


class SubTransaction(object):
    def __init__(self, doc):
        self.doc = doc

    def Start(self):
        self.mark = len(self.doc.pending)

    def Commit(self):
        pass

    def RollBack(self):
        del self.doc.pending[self.mark:]


class Line(object):
    @staticmethod
    def CreateBound(a, b):
        return (a, b)


class ElementTransformUtils(object):
    @staticmethod
    def RotateElement(doc, eid, axis, angle):
        for e in doc.pending:
            if e.Id == eid:
                e.rotation += angle


def install():
    db = types.ModuleType("Autodesk.Revit.DB")
    for name, obj in list(globals().items()):
        if isinstance(obj, (type, Enum)) or name in ("XYZ",):
            setattr(db, name, obj)
    structure = types.ModuleType("Autodesk.Revit.DB.Structure")
    structure.StructuralType = StructuralType
    autodesk = types.ModuleType("Autodesk")
    revit = types.ModuleType("Autodesk.Revit")
    autodesk.Revit, revit.DB, db.Structure = revit, db, structure
    sys.modules.update({"Autodesk": autodesk, "Autodesk.Revit": revit,
                        "Autodesk.Revit.DB": db, "Autodesk.Revit.DB.Structure": structure})
    # hosting.py is replaced by a fake in the tests; stub its import.
    hosting = types.ModuleType("cad2revit.hosting")
    hosting.HostFinder = None
    hosting.create_temp_view = lambda doc: None
    sys.modules["cad2revit.hosting"] = hosting
